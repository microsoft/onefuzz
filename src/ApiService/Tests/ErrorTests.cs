using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class ErrorTests {

    [Fact]
    public void JsonHasErrorTitle() {
        var error = Error.Create(ErrorCode.INVALID_IMAGE);
        var json = JsonSerializer.Serialize(error);
        Assert.Equal(@"{""Code"":463,""Errors"":[],""Title"":""INVALID_IMAGE""}", json);
    }
}
