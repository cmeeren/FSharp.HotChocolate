namespace HotChocolate

open System.Threading.Tasks
open HotChocolate.Resolvers
open HotChocolate.Types.Descriptors
open Microsoft.FSharp.Core
open HotChocolate.Configuration
open HotChocolate.Types.Descriptors.Definitions


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


    let convertObjectAsyncToTask (typeInspector: ITypeInspector) (fieldDef: ObjectFieldDefinition) =
        match
            fieldDef.ResultType
            |> Option.ofObj
            |> Option.bind Reflection.tryGetInnerAsyncType
        with
        | None -> ()
        | Some innerType ->
            match fieldDef.Type with
            | :? ExtendedTypeReference as extendedTypeRef ->
                if extendedTypeRef.Type.Type = fieldDef.ResultType then
                    let finalResultType = typedefof<Task<_>>.MakeGenericType([| innerType |])
                    let finalType = typeInspector.GetType(finalResultType)
                    fieldDef.Type <- extendedTypeRef.WithType(finalType)
            | _ -> ()

            fieldDef.MiddlewareDefinitions.Add(
                FieldMiddlewareDefinition(fun next -> convertAsyncToTaskMiddleware innerType next)
            )


    let convertInterfaceAsyncToTask (typeInspector: ITypeInspector) (fieldDef: InterfaceFieldDefinition) =
        let resultType =
            fieldDef.ResultType
            |> Option.ofObj
            |> Option.orElseWith (fun () ->
                match fieldDef.Type with
                | :? ExtendedTypeReference as extendedTypeRef -> Some extendedTypeRef.Type.Type
                | _ -> None
            )

        match resultType with
        | None -> ()
        | Some resultType ->
            match Reflection.tryGetInnerAsyncType resultType with
            | None -> ()
            | Some innerType ->
                match fieldDef.Type with
                | :? ExtendedTypeReference as extendedTypeRef ->
                    if extendedTypeRef.Type.Type = resultType then
                        let finalResultType = typedefof<Task<_>>.MakeGenericType([| innerType |])
                        let finalType = typeInspector.GetType(finalResultType)
                        fieldDef.Type <- extendedTypeRef.WithType(finalType)
                | _ -> ()

                fieldDef.MiddlewareDefinitions.Add(
                    FieldMiddlewareDefinition(fun next -> convertAsyncToTaskMiddleware innerType next)
                )


/// This type interceptor adds support for Async<_> fields.
type FSharpAsyncTypeInterceptor() =
    inherit TypeInterceptor()

    override this.OnBeforeRegisterDependencies(discoveryContext, definition) =
        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields
            |> Seq.iter (convertObjectAsyncToTask discoveryContext.TypeInspector)
        | :? InterfaceTypeDefinition as objectDef ->
            objectDef.Fields
            |> Seq.iter (convertInterfaceAsyncToTask discoveryContext.TypeInspector)
        | _ -> ()
