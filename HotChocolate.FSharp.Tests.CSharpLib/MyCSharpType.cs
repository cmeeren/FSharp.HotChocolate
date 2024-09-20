using HotChocolate.FSharp.Tests.FSharpLib;
using HotChocolate.Types;

namespace HotChocolate.FSharp.Tests.CSharpLib;

public class MyCSharpType
{
  public int CSharpDefinedInt => 1;

  public int? CSharpDefinedNullableInt => 1;

  public string CSharpDefinedString => "1";

  public string? CSharpDefinedNullableString => "1";

  [UsePaging(ConnectionName = "MyCSharpTypePagedString", AllowBackwardPagination = false)]
  public List<string> PagedString => ["1"];

  [UsePaging(ConnectionName = "MyCSharpTypePagedNullableString", AllowBackwardPagination = false)]
  public List<string?> PagedNullableString => ["1"];

  [UsePaging(ConnectionName = "MyCSharpTypePagedMyCSharpType", AllowBackwardPagination = false)]
  public List<MyCSharpType> PagedMyCSharpType => [new()];

  [UsePaging(ConnectionName = "MyCSharpTypePagedNullableMyCSharpType", AllowBackwardPagination = false)]
  public List<MyCSharpType?> PagedNullableMyCSharpType => [new()];

  [UsePaging(ConnectionName = "MyCSharpTypePagedMyFSharpType", AllowBackwardPagination = false)]
  public List<MyFSharpType> PagedMyFSharpType => [new()];

  [UsePaging(ConnectionName = "MyCSharpTypePagedNullableMyFSharpType", AllowBackwardPagination = false)]
  public List<MyFSharpType?> PagedNullableMyFSharpType => [new()];
}

[ExtendObjectType(typeof(MyCSharpType))]
public class MyCSharpTypeCSharpExtensions
{
  public int CSharpDefinedExtensionInt => 1;

  public int? CSharpDefinedExtensionNullableInt => 1;

  public string CSharpDefinedExtensionString => "1";

  public string? CSharpDefinedExtensionNullableString => "1";
}
