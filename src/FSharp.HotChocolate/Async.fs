namespace HotChocolate

open System
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks
open HotChocolate.Configuration
open HotChocolate.Language
open HotChocolate.Resolvers
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations


[<AutoOpen>]
module private AsyncHelpers =


    type AsyncFieldReturnShape =
        | Async of innerType: Type
        | CancellableTaskLike of Reflection.CancellableTaskLikeResolverShape


    type AsyncResultConversion = {
        StartImmediateAsTask: obj -> CancellationToken option -> obj
        ReadResult: Task -> obj
        FormatResult: obj -> obj
    }


    let reraiseInComputationExpression (ex: Exception) =
        ExceptionDispatchInfo.Capture(ex).Throw()
        failwith "This code should never be reached"


    let tryGetAsyncFieldReturnShape resultType =
        match Reflection.tryGetInnerAsyncType resultType with
        | Some innerType -> Some(Async innerType)
        | None ->
            Reflection.tryGetCancellableTaskLikeResolverShape resultType
            |> Option.map CancellableTaskLike


    let getFieldResultType =
        function
        | Async innerType -> typedefof<Task<_>>.MakeGenericType([| innerType |])
        | CancellableTaskLike cancellable -> cancellable.AwaitableType


    let clearObjectPureResolver (cfg: ObjectFieldConfiguration) =
        match cfg.PureResolver with
        | null -> ()
        | pureResolver ->
            if isNull cfg.Resolver then
                cfg.Resolver <- FieldResolverDelegate(fun context -> ValueTask<obj>(pureResolver.Invoke(context)))

            cfg.PureResolver <- null


    let clearInterfacePureResolver (cfg: InterfaceFieldConfiguration) =
        match cfg.PureResolver with
        | null -> ()
        | pureResolver ->
            if isNull cfg.Resolver then
                cfg.Resolver <- FieldResolverDelegate(fun context -> ValueTask<obj>(pureResolver.Invoke(context)))

            cfg.PureResolver <- null


    let clearObjectPureResolverIfAsync (cfg: ObjectFieldConfiguration) =
        cfg.ResultType
        |> Option.ofObj
        |> Option.bind tryGetAsyncFieldReturnShape
        |> Option.iter (fun _ -> clearObjectPureResolver cfg)


    let clearInterfacePureResolverIfAsync (cfg: InterfaceFieldConfiguration) =
        let resultType =
            cfg.ResultType
            |> Option.ofObj
            |> Option.orElseWith (fun () ->
                match cfg.Type with
                | :? ExtendedTypeReference as extendedTypeRef -> Some extendedTypeRef.Type.Type
                | _ -> None
            )

        resultType
        |> Option.bind tryGetAsyncFieldReturnShape
        |> Option.iter (fun _ -> clearInterfacePureResolver cfg)


    let formatAsyncResult result =
        result |> Reflection.unwrapOption |> Reflection.unwrapUnion


    let formatPagedAsyncResult result =
        result
        |> formatAsyncResult
        |> Reflection.boxValueTypeEnumerableAsReferenceEnumerable


    let getTaskResult (readResult: Task -> obj) (formatResult: obj -> obj) (resultTask: Task) =
        task {
            do! resultTask

            return resultTask |> readResult |> formatResult
        }


    let getAsyncResultConversion formatResult innerType = {
        StartImmediateAsTask = Reflection.asyncStartImmediateAsTask innerType
        ReadResult = Reflection.taskResult innerType
        FormatResult = formatResult
    }


    let setResultTask (resultTask: Task<obj>) (context: IMiddlewareContext) =
        task {
            let! result = resultTask
            context.Result <- result
        }


    let setTaskResult (readResult: Task -> obj) (resultTask: Task) (context: IMiddlewareContext) =
        setResultTask (getTaskResult readResult formatAsyncResult resultTask) context


    let createAsyncResultTask (conversion: AsyncResultConversion) (cancellationToken: CancellationToken) (result: obj) =
        let task = conversion.StartImmediateAsTask result (Some cancellationToken) :?> Task

        getTaskResult conversion.ReadResult conversion.FormatResult task


    let setAsyncResult (conversion: AsyncResultConversion) (context: IMiddlewareContext) =
        setResultTask (createAsyncResultTask conversion context.RequestAborted context.Result) context


    let setCancellableTaskLikeResult
        (cancellable: Reflection.CancellableTaskLikeResolverShape)
        (context: IMiddlewareContext)
        =
        let awaitable = cancellable.Invoke context.Result context.RequestAborted
        let task = cancellable.AwaitableAsTask awaitable

        setTaskResult cancellable.ReadTaskResult task context


    module AsyncNodeResolver =


        let private tryCreateAsyncResultTask (cancellationToken: CancellationToken) =
            function
            | null -> None
            | result ->
                match Reflection.tryGetInnerAsyncType (result.GetType()) with
                | Some innerType ->
                    let conversion = getAsyncResultConversion formatAsyncResult innerType

                    createAsyncResultTask conversion cancellationToken result |> Some
                | None -> None


        let private tryStartAsyncElementTasks (context: IMiddlewareContext) (results: obj[]) =
            let mutable asyncElements: ResizeArray<int * Task<obj>> option = None

            for i = 0 to results.Length - 1 do
                match tryCreateAsyncResultTask context.RequestAborted results[i] with
                | Some resultTask ->
                    let elements =
                        match asyncElements with
                        | Some elements -> elements
                        | None ->
                            let elements = ResizeArray()
                            asyncElements <- Some elements
                            elements

                    elements.Add((i, resultTask))
                | None -> ()

            asyncElements


        let private setElementResult (context: IMiddlewareContext) (results: obj[]) index (result: obj) =
            match result with
            | :? IError as error ->
                results[index] <- null
                context.ReportError(error.WithPath(context.Path.Append(index)))
            | _ -> results[index] <- result


        let private createArrayResultTask
            (context: IMiddlewareContext)
            (results: obj[])
            (asyncElements: ResizeArray<int * Task<obj>>)
            =
            task {
                let results = Array.copy results

                for elementIndex = 0 to asyncElements.Count - 1 do
                    let i, resultTask = asyncElements[elementIndex]

                    try
                        let! result = resultTask
                        setElementResult context results i result
                    with
                    | :? OperationCanceledException as ex -> reraiseInComputationExpression ex
                    | ex ->
                        results[i] <- null
                        context.ReportError(ex, fun error -> error.SetPath(context.Path.Append(i)) |> ignore)

                return results :> obj
            }


        let private tryCreateAsyncArrayResultTask (context: IMiddlewareContext) (results: obj[]) =
            match tryStartAsyncElementTasks context results with
            | Some resultTasks -> createArrayResultTask context results resultTasks |> Some
            | None -> None


        let private tryCreateResultTask (context: IMiddlewareContext) =
            match context.Result with
            | :? (obj[]) as results -> tryCreateAsyncArrayResultTask context results
            | result -> tryCreateAsyncResultTask context.RequestAborted result


        let private setResult (context: IMiddlewareContext) =
            match tryCreateResultTask context with
            | Some resultTask ->
                task {
                    let! result = resultTask
                    context.Result <- result
                }
                |> ValueTask
            | None -> ValueTask()


        let private completeResult (nextTask: ValueTask) (context: IMiddlewareContext) =
            task {
                do! nextTask
                do! setResult context
            }
            |> ValueTask


        let convertResultMiddleware (next: FieldDelegate) (context: IMiddlewareContext) =
            let nextTask = next.Invoke(context)

            if nextTask.IsCompletedSuccessfully then
                nextTask.GetAwaiter().GetResult()
                setResult context
            else
                completeResult nextTask context


    // HotChocolate treats Async<_> as a pure sync result unless we clear the pure resolver after resolver
    // compilation. The normal resolver is still needed so the async conversion middleware can run.
    let convertAsyncToTaskMiddleware
        (conversion: AsyncResultConversion)
        (next: FieldDelegate)
        (context: IMiddlewareContext)
        =
        task {
            do! next.Invoke(context)
            do! setAsyncResult conversion context
        }
        |> ValueTask


    let convertCancellableTaskLikeMiddleware
        (cancellable: Reflection.CancellableTaskLikeResolverShape)
        (next: FieldDelegate)
        (context: IMiddlewareContext)
        =
        task {
            do! next.Invoke(context)
            do! setCancellableTaskLikeResult cancellable context
        }
        |> ValueTask


    let convertAsyncFieldMiddleware asyncFormatResult returnShape =
        match returnShape with
        | Async innerType ->
            let conversion = getAsyncResultConversion asyncFormatResult innerType

            FieldMiddlewareConfiguration(fun next -> convertAsyncToTaskMiddleware conversion next)
        | CancellableTaskLike cancellable ->
            FieldMiddlewareConfiguration(fun next -> convertCancellableTaskLikeMiddleware cancellable next)


    let hasPagingMiddleware (middlewareConfigurations: seq<FieldMiddlewareConfiguration>) =
        middlewareConfigurations
        |> Seq.exists (fun middleware -> middleware.Key = WellKnownMiddleware.Paging)


    let shouldExposeResultTypeForPaging middlewareConfigurations =
        function
        | Async _ -> hasPagingMiddleware middlewareConfigurations
        | CancellableTaskLike _ -> false


    let convertObjectAsyncToTask (typeInspector: ITypeInspector) (cfg: ObjectFieldConfiguration) =
        match cfg.ResultType |> Option.ofObj |> Option.bind tryGetAsyncFieldReturnShape with
        | None -> ()
        | Some returnShape ->
            let originalResultType = cfg.ResultType
            let fieldResultType = getFieldResultType returnShape

            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                if extendedTypeRef.Type.Type = originalResultType then
                    let finalType = typeInspector.GetType(fieldResultType)
                    cfg.Type <- extendedTypeRef.WithType(finalType)
            | _ -> ()

            let exposeResultTypeForPaging =
                shouldExposeResultTypeForPaging cfg.MiddlewareConfigurations returnShape

            if exposeResultTypeForPaging then
                cfg.ResultType <- fieldResultType

            let formatResult =
                if exposeResultTypeForPaging then
                    formatPagedAsyncResult
                else
                    formatAsyncResult

            cfg.MiddlewareConfigurations.Add(convertAsyncFieldMiddleware formatResult returnShape)

            clearObjectPureResolver cfg


    let convertInterfaceAsyncToTask (typeInspector: ITypeInspector) (cfg: InterfaceFieldConfiguration) =
        let resultType =
            cfg.ResultType
            |> Option.ofObj
            |> Option.orElseWith (fun () ->
                match cfg.Type with
                | :? ExtendedTypeReference as extendedTypeRef -> Some extendedTypeRef.Type.Type
                | _ -> None
            )

        match resultType with
        | None -> ()
        | Some resultType ->
            match tryGetAsyncFieldReturnShape resultType with
            | None -> ()
            | Some returnShape ->
                let fieldResultType = getFieldResultType returnShape

                match cfg.Type with
                | :? ExtendedTypeReference as extendedTypeRef ->
                    if extendedTypeRef.Type.Type = resultType then
                        let finalType = typeInspector.GetType(fieldResultType)
                        cfg.Type <- extendedTypeRef.WithType(finalType)
                | _ -> ()

                cfg.MiddlewareDefinitions.Add(convertAsyncFieldMiddleware formatAsyncResult returnShape)

                clearInterfacePureResolver cfg


module internal FSharpAsyncMiddleware =


    let convertNodeResolverAsyncResult =
        FieldMiddleware(fun next ->
            FieldDelegate(fun context -> AsyncNodeResolver.convertResultMiddleware next context)
        )


/// Static [<Node>] resolver pipelines are not exposed as normal field configurations. This interceptor applies a narrow
/// post-node-field middleware instead of global field middleware.
type internal FSharpAsyncNodeResolverTypeInterceptor() =
    inherit TypeInterceptor()

    let middlewareKey = "FSharp.HotChocolate.AsyncNodeResolver"
    let mutable queryTypeConfig: ObjectTypeConfiguration option = None

    let isNodeField (field: ObjectFieldConfiguration) =
        field.Name = "node"
        && field.Arguments.Count = 1
        && field.Arguments[0].Name = "id"

    let isNodesField (field: ObjectFieldConfiguration) =
        field.Name = "nodes"
        && field.Arguments.Count = 1
        && field.Arguments[0].Name = "ids"

    let tryAddMiddleware (field: ObjectFieldConfiguration) =
        if
            not (
                field.MiddlewareConfigurations
                |> Seq.exists (fun middleware -> middleware.Key = middlewareKey)
            )
        then
            let middleware =
                FieldMiddlewareConfiguration(FSharpAsyncMiddleware.convertNodeResolverAsyncResult, false, middlewareKey)

            field.MiddlewareConfigurations.Insert(0, middleware)

    override _.OnAfterResolveRootType(_, config, operationType) =
        if operationType = OperationType.Query then
            queryTypeConfig <- Some config

    override _.OnBeforeCompleteType(_, config) =
        match config with
        | :? ObjectTypeConfiguration as config when
            queryTypeConfig
            |> Option.exists (fun queryConfig -> Object.ReferenceEquals(config, queryConfig))
            ->
            config.Fields
            |> Seq.filter (fun field -> isNodeField field || isNodesField field)
            |> Seq.iter tryAddMiddleware
        | _ -> ()


/// This type interceptor adds support for Async<_> and CancellationToken -> Task<_>/ValueTask<_> fields.
type internal FSharpAsyncTypeInterceptor() =
    inherit TypeInterceptor()

    override this.OnBeforeRegisterDependencies(discoveryContext, config) =
        match config with
        | :? ObjectTypeConfiguration as cfg ->
            cfg.Fields |> Seq.iter (convertObjectAsyncToTask discoveryContext.TypeInspector)
        | :? InterfaceTypeConfiguration as cfg ->
            cfg.Fields
            |> Seq.iter (convertInterfaceAsyncToTask discoveryContext.TypeInspector)
        | _ -> ()

    override this.OnBeforeCompleteType(_, config) =
        match config with
        | :? ObjectTypeConfiguration as cfg -> cfg.Fields |> Seq.iter clearObjectPureResolverIfAsync
        | :? InterfaceTypeConfiguration as cfg -> cfg.Fields |> Seq.iter clearInterfacePureResolverIfAsync
        | _ -> ()
