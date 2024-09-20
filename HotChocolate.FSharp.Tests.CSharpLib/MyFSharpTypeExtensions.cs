using HotChocolate.FSharp.Tests.FSharpLib;
using HotChocolate.Types;

namespace HotChocolate.FSharp.Tests.CSharpLib;

[ExtendObjectType(typeof(MyFSharpType))]
public class MyFSharpTypeCSharpExtensions
{
  public int CSharpDefinedExtensionInt => 1;

  public int? CSharpDefinedExtensionNullableInt => 1;

  public string CSharpDefinedExtensionString => "1";

  public string? CSharpDefinedExtensionNullableString => "1";
}
