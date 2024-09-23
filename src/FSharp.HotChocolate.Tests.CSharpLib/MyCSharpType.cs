using FSharp.HotChocolate.Tests.FSharpLib;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;

namespace FSharp.HotChocolate.Tests.CSharpLib;

public class MyCSharpType
{
  public int CSharpDefinedInt => 1;

  public int? CSharpDefinedNullableInt => 1;

  public string CSharpDefinedString => "1";

  public string? CSharpDefinedNullableString => "1";

  [UsePaging(ConnectionName = "MyCSharpTypePagedString", AllowBackwardPagination = false)]
  public List<string> PagedString => ["1"];

  [UsePaging(ConnectionName = "MyCSharpTypePagedNullableString", AllowBackwardPagination = false)]
  public List<string?> PagedNullableString => ["1", null];

  [UsePaging(ConnectionName = "MyCSharpTypePagedMyCSharpType", AllowBackwardPagination = false)]
  public List<MyCSharpType> PagedMyCSharpType => [new()];

  [UsePaging(ConnectionName = "MyCSharpTypePagedNullableMyCSharpType", AllowBackwardPagination = false)]
  public List<MyCSharpType?> PagedNullableMyCSharpType => [new(), null];

  [UsePaging(ConnectionName = "MyCSharpTypePagedMyFSharpType", AllowBackwardPagination = false)]
  public List<MyFSharpType> PagedMyFSharpType => [new()];

  [UsePaging(ConnectionName = "MyCSharpTypePagedNullableMyFSharpType", AllowBackwardPagination = false)]
  public List<MyFSharpType?> PagedNullableMyFSharpType => [new(), null];

  [UsePaging(ConnectionName = "MyCSharpTypeCustomPagedString", AllowBackwardPagination = false)]
  public Connection<string> CustomPagedString =>
    new(
      new List<Edge<string>> { new("1", "a") },
      new ConnectionPageInfo(false, false, "a", "a")
    );

  [UsePaging(ConnectionName = "MyCSharpTypeCustomPagedNullableString", AllowBackwardPagination = false)]
  public Connection<string?> CustomPagedNullableString =>
    new(
      new List<Edge<string?>> { new("1", "a"), new(null, "b") },
      new ConnectionPageInfo(false, false, "a", "b")
    );

  [UsePaging(ConnectionName = "MyCSharpTypeCustomPagedMyCSharpType", AllowBackwardPagination = false)]
  public Connection<MyCSharpType> CustomPagedMyCSharpType =>
    new(
      new List<Edge<MyCSharpType>> { new(new MyCSharpType(), "a") },
      new ConnectionPageInfo(false, false, "a", "a")
    );

  [UsePaging(ConnectionName = "MyCSharpTypeCustomPagedNullableMyCSharpType", AllowBackwardPagination = false)]
  public Connection<MyCSharpType?> CustomPagedNullableMyCSharpType =>
    new(
      new List<Edge<MyCSharpType?>> { new(new MyCSharpType(), "a"), new(null, "b") },
      new ConnectionPageInfo(false, false, "a", "b")
    );

  [UsePaging(ConnectionName = "MyCSharpTypeCustomPagedMyFSharpType", AllowBackwardPagination = false)]
  public Connection<MyFSharpType> CustomPagedMyFSharpType =>
    new(
      new List<Edge<MyFSharpType>> { new(new MyFSharpType(), "a") },
      new ConnectionPageInfo(false, false, "a", "a")
    );

  [UsePaging(ConnectionName = "MyCSharpTypeCustomPagedNullableMyFSharpType", AllowBackwardPagination = false)]
  public Connection<MyFSharpType?> CustomPagedNullableMyFSharpType =>
    new(
      new List<Edge<MyFSharpType?>> { new(new MyFSharpType(), "a"), new(null, "b") },
      new ConnectionPageInfo(false, false, "a", "b")
    );
}

[ExtendObjectType(typeof(MyCSharpType))]
public class MyCSharpTypeCSharpExtensions
{
  public int CSharpDefinedExtensionInt => 1;

  public int? CSharpDefinedExtensionNullableInt => 1;

  public string CSharpDefinedExtensionString => "1";

  public string? CSharpDefinedExtensionNullableString => "1";
}
