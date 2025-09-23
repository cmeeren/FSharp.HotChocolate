namespace HotChocolate

open System.Threading.Tasks
open HotChocolate.Configuration
open HotChocolate.Resolvers
open HotChocolate.Types.Descriptors
open HotChocolate.Types.Descriptors.Configurations


[<AutoOpen>]
module private AsyncHelpers =


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


    let convertObjectAsyncToTask (typeInspector: ITypeInspector) (cfg: ObjectFieldConfiguration) =
        match cfg.ResultType |> Option.ofObj |> Option.bind Reflection.tryGetInnerAsyncType with
        | None -> ()
        | Some innerType ->
            match cfg.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                if extendedTypeRef.Type.Type = cfg.ResultType then
                    let finalResultType = typedefof<Task<_>>.MakeGenericType([| innerType |])
                    let finalType = typeInspector.GetType(finalResultType)
                    cfg.Type <- extendedTypeRef.WithType(finalType)
            | _ -> ()

            cfg.MiddlewareConfigurations.Add(
                FieldMiddlewareConfiguration(fun next -> convertAsyncToTaskMiddleware innerType next)
            )


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
            match Reflection.tryGetInnerAsyncType resultType with
            | None -> ()
            | Some innerType ->
                match cfg.Type with
                | :? ExtendedTypeReference as extendedTypeRef ->
                    if extendedTypeRef.Type.Type = resultType then
                        let finalResultType = typedefof<Task<_>>.MakeGenericType([| innerType |])
                        let finalType = typeInspector.GetType(finalResultType)
                        cfg.Type <- extendedTypeRef.WithType(finalType)
                | _ -> ()

                cfg.MiddlewareDefinitions.Add(
                    FieldMiddlewareConfiguration(fun next -> convertAsyncToTaskMiddleware innerType next)
                )


/// This type interceptor adds support for Async<_> fields.
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
