using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class ControlObservationParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"operationId\":\"op-1\",\"relativeMilliseconds\":1,\"source\":\"bogus\"}")]
    [InlineData("{\"operationId\":\"op-1\",\"relativeMilliseconds\":1,\"source\":\"userModeCall\",\"api\":\"HidD_SetFeature\",\"direction\":\"input\",\"status\":\"pending\",\"vendorId\":5426,\"productId\":1,\"interfaceLabel\":\"hid-1\",\"payloadDisposition\":\"redactedUnproven\",\"bufferHex\":\"ABC\"}")]
    public void TryParseUtf8_MalformedEvent_ReturnsFailure(string json)
    {
        var parsed = ControlObservationParser.TryParseUtf8(
            Encoding.UTF8.GetBytes(json),
            out var observation,
            out var error);

        Assert.False(parsed);
        Assert.Null(observation);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("relativeMilliseconds", "\"wrong\"")]
    [InlineData("nativeStatus", "{}")]
    [InlineData("usagePage", "[]")]
    [InlineData("reportId", "true")]
    [InlineData("completedRelativeMilliseconds", "{}")]
    [InlineData("bufferHex", "123")]
    public void TryParseUtf8_WrongJsonKinds_ReturnFailureInsteadOfThrowing(string field, string replacement)
    {
        var json = $$"""
            {
              "operationId": "op-1",
              "relativeMilliseconds": {{(field == "relativeMilliseconds" ? replacement : "27")}},
              "source": "userModeCall",
              "api": "HidD_SetFeature",
              "direction": "input",
              "status": "pending",
              "nativeStatus": {{(field == "nativeStatus" ? replacement : "null")}},
              "errorCategory": null,
              "vendorId": 5426,
              "productId": 1,
              "interfaceLabel": "hid-a1",
              "usagePage": {{(field == "usagePage" ? replacement : "1")}},
              "usage": 2,
              "reportId": {{(field == "reportId" ? replacement : "0")}},
              "completedRelativeMilliseconds": {{(field == "completedRelativeMilliseconds" ? replacement : "28")}},
              "bufferHex": {{(field == "bufferHex" ? replacement : "\"00\"")}},
              "payloadDisposition": "redactedUnproven"
            }
            """;

        var exception = Record.Exception(() => ControlObservationParser.TryParseUtf8(
            Encoding.UTF8.GetBytes(json),
            out _,
            out _));

        Assert.Null(exception);
        Assert.False(ControlObservationParser.TryParseUtf8(
            Encoding.UTF8.GetBytes(json),
            out _,
            out _));
    }

    [Fact]
    public void TryParseUtf8_ValidUnknownShape_PreservesBoundedShape()
    {
        const string json = """
            {
              "operationId": "op-1",
              "relativeMilliseconds": 27,
              "source": "userModeCall",
              "api": "HidD_SetFeature",
              "direction": "input",
              "status": "pending",
              "nativeStatus": null,
              "errorCategory": null,
              "vendorId": "1532",
              "productId": "beef",
              "interfaceLabel": "hid-a1",
              "usagePage": 1,
              "usage": 2,
              "reportId": 0,
              "completedRelativeMilliseconds": 31.5,
              "bufferHex": "0001020304",
              "payloadDisposition": "redactedUnproven"
            }
            """;

        Assert.True(ControlObservationParser.TryParseUtf8(
            Encoding.UTF8.GetBytes(json),
            out var observation,
            out var error));
        Assert.Null(error);
        Assert.NotNull(observation);
        Assert.Equal(5, observation.Buffer.Length);
        Assert.Equal(ObservationStatus.Pending, observation.Status);
        Assert.Equal((ushort)0xBEEF, observation.ProductId);
        Assert.Equal(31.5, observation.CompletedRelativeMilliseconds);
    }

    [Fact]
    public void TryParseUtf8_NonFiniteCompletionTimestamp_ReturnsFailure()
    {
        const string json = """
            {
              "operationId": "op-1",
              "relativeMilliseconds": 27,
              "source": "userModeCall",
              "api": "HidD_SetFeature",
              "direction": "input",
              "status": "succeeded",
              "vendorId": 5426,
              "productId": 1,
              "interfaceLabel": "hid-a1",
              "completedRelativeMilliseconds": 1e999,
              "payloadDisposition": "redactedUnproven"
            }
            """;

        Assert.False(ControlObservationParser.TryParseUtf8(
            Encoding.UTF8.GetBytes(json),
            out _,
            out _));
    }

    [Fact]
    public void TryParseUtf8_RegressingCompletionTimestamp_ReturnsFailure()
    {
        const string json = """
            {
              "operationId": "op-1",
              "relativeMilliseconds": 27,
              "source": "userModeCall",
              "api": "HidD_SetFeature",
              "direction": "input",
              "status": "succeeded",
              "vendorId": 5426,
              "productId": 1,
              "interfaceLabel": "hid-a1",
              "completedRelativeMilliseconds": 26.99,
              "payloadDisposition": "redactedUnproven"
            }
            """;

        Assert.False(ControlObservationParser.TryParseUtf8(
            Encoding.UTF8.GetBytes(json),
            out _,
            out _));
    }
}
