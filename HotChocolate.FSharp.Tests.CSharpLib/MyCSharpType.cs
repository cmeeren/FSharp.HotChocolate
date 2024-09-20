using HotChocolate.Types;

namespace HotChocolate.FSharp.Tests.CSharpLib;

public class MyCSharpType
{
  public int CSharpDefinedInt => 1;

  public int? CSharpDefinedNullableInt => 1;

  public string CSharpDefinedString => "1";

  public string? CSharpDefinedNullableString => "1";
}

[ExtendObjectType(typeof(MyCSharpType))]
public class MyCSharpTypeCSharpExtensions
{
  public int CSharpDefinedExtensionInt => 1;

  public int? CSharpDefinedExtensionNullableInt => 1;

  public string CSharpDefinedExtensionString => "1";

  public string? CSharpDefinedExtensionNullableString => "1";
}
