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
            let result = Reflection.taskResult innerType task

            let result =
                if isNull result then
                    result
                else
                    match getUnwrapOptionFormatter (result.GetType()) with
                    | None -> result
                    | Some format -> format result

            context.Result <- result
        }
        |> ValueTask


    let convertAsyncToTask (typeInspector: ITypeInspector) (fieldDef: ObjectFieldDefinition) =
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

            fieldDef.MiddlewareDefinitions.Insert(
                0,
                FieldMiddlewareDefinition(fun next -> convertAsyncToTaskMiddleware innerType next)
            )


/// This type interceptor adds support for Async<_> fields.
type FSharpAsyncTypeInterceptor() =
    inherit TypeInterceptor()

    override this.OnBeforeRegisterDependencies(discoveryContext, definition) =
        match definition with
        | :? ObjectTypeDefinition as objectDef ->
            objectDef.Fields |> Seq.iter (convertAsyncToTask discoveryContext.TypeInspector)
        | _ -> ()
