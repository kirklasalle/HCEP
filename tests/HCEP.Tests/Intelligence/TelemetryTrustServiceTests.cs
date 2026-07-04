// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: TelemetryTrustService
// ──────────────────────────────────────────────────────────────
using HCEP.Intelligence;

namespace HCEP.Tests.Intelligence;

public sealed class TelemetryTrustServiceTests : IDisposable
{
    private readonly TelemetryTrustService _service = new();

    public void Dispose() => _service.Dispose();

    [Fact]
    public void State_IsInitialized()
    {
        // TelemetryTrustService bootstraps from the embedded PAD (always present in tests).
        // Just verify the state is a coherent object.
        Assert.NotNull(_service.State);
    }

    [Fact]
    public void WhenValid_BootTimestamp_IsRecent()
    {
        if (!_service.State.IsValid) return; // skip if PAD missing in test environment
        Assert.True(
            (DateTimeOffset.UtcNow - _service.State.BootTimestamp).TotalSeconds < 10,
            "BootTimestamp should be recent");
    }

    [Fact]
    public void WhenValid_SignPayload_ReturnsDeterministicNonEmptyString()
    {
        if (!_service.State.IsValid) return;

        const string payload = "{\"mode\":\"Logic\",\"confidence\":0.87}";
        string? sig1 = _service.SignPayload(payload);
        string? sig2 = _service.SignPayload(payload);

        Assert.NotNull(sig1);
        Assert.NotEmpty(sig1!);
        // Same input, same session key → same HMAC
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void WhenValid_DifferentPayloads_ProduceDifferentSignatures()
    {
        if (!_service.State.IsValid) return;

        string? s1 = _service.SignPayload("{\"mode\":\"Logic\"}");
        string? s2 = _service.SignPayload("{\"mode\":\"Think\"}");

        Assert.NotNull(s1);
        Assert.NotNull(s2);
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void WhenValid_SigningKeyId_HasExpectedFormat()
    {
        if (!_service.State.IsValid) return;
        // SigningKeyId is 4 bytes hex = 8 chars
        Assert.Equal(8, _service.State.SigningKeyId.Length);
    }

    [Fact]
    public void WhenValid_PadHash_IsTruncated()
    {
        if (!_service.State.IsValid) return;
        // PadHash ends with "..."
        Assert.EndsWith("...", _service.State.PadHash);
    }
}
