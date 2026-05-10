namespace HotChocolate

open System.Threading.Tasks
open HotChocolate.Configuration
open HotChocolate.Resolvers
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations


[<AutoOpen>]
module private AsyncHelpers =


    type AsyncFieldReturnShape =
        | Async of innerType: System.Type
        | CancellableTaskLike of Reflection.CancellableTaskLikeResolverShape


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


    // HotChocolate treats Async<_> as a pure sync result unless we clear the pure resolver after resolver
    // compilation. The normal resolver is still needed so the async conversion middleware can run.
    let convertAsyncToTaskMiddleware innerType (next: FieldDelegate) (context: IMiddlewareContext) =
        task {
            do! next.Invoke(context)

            let task =
                Reflection.asyncStartImmediateAsTask innerType context.Result (Some context.RequestAborted) :?> Task

            do! task

            context.Result <-
                task
                |> Reflection.taskResult innerType
                |> Reflection.unwrapOption
                |> Reflection.unwrapUnion
        }
        |> ValueTask


    let convertCancellableTaskLikeMiddleware
        (cancellable: Reflection.CancellableTaskLikeResolverShape)
        (next: FieldDelegate)
        (context: IMiddlewareContext)
        =
        task {
            do! next.Invoke(context)

            let awaitable = cancellable.Invoke context.Result context.RequestAborted
            let task = cancellable.AwaitableAsTask awaitable

            do! task

            context.Result <-
                task
                |> cancellable.ReadTaskResult
                |> Reflection.unwrapOption
                |> Reflection.unwrapUnion
        }
        |> ValueTask


    let convertAsyncFieldMiddleware returnShape =
        match returnShape with
        | Async innerType -> FieldMiddlewareConfiguration(fun next -> convertAsyncToTaskMiddleware innerType next)
        | CancellableTaskLike cancellable ->
            FieldMiddlewareConfiguration(fun next -> convertCancellableTaskLikeMiddleware cancellable next)


    let convertObjectAsyncToTask (typeInspector: ITypeInspector) (cfg: ObjectFieldConfiguration) =
        match cfg.ResultType |> Option.ofObj |> Option.bind tryGetAsyncFieldReturnShape with
        | None -> ()
        | Some returnShape ->
            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                if extendedTypeRef.Type.Type = cfg.ResultType then
                    let finalType = typeInspector.GetType(getFieldResultType returnShape)
                    cfg.Type <- extendedTypeRef.WithType(finalType)
            | _ -> ()

            cfg.MiddlewareConfigurations.Add(convertAsyncFieldMiddleware returnShape)

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
                match cfg.Type with
                | :? ExtendedTypeReference as extendedTypeRef ->
                    if extendedTypeRef.Type.Type = resultType then
                        let finalType = typeInspector.GetType(getFieldResultType returnShape)
                        cfg.Type <- extendedTypeRef.WithType(finalType)
                | _ -> ()

                cfg.MiddlewareDefinitions.Add(convertAsyncFieldMiddleware returnShape)

                clearInterfacePureResolver cfg


/// This type interceptor adds support for Async<_> and CancellationToken -> Task<_>/ValueTask<_> fields.
type FSharpAsyncTypeInterceptor() =
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
