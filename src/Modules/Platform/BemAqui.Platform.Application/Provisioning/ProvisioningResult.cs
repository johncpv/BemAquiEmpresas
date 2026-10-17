namespace BemAqui.Platform.Application.Provisioning;

public sealed record ProvisioningStepResult(string Step, string Status, TimeSpan Duration, string? Message);

public sealed record ProvisioningResult(bool Success, Guid? TenantId, IReadOnlyList<ProvisioningStepResult> Steps, string? Error, string? NextStep);

public static class ProvisioningSteps
{
    public const string Register = "Register";
    public const string CreateRole = "CreateRole";
    public const string CreateDatabase = "CreateDatabase";
    public const string Migrate = "Migrate";
    public const string Seed = "Seed";
    public const string CreateOwner = "CreateOwner";
    public const string SendInvite = "SendInvite";
    public const string Activate = "Activate";

    public static readonly string[] Ordered = [Register, CreateRole, CreateDatabase, Migrate, Seed, CreateOwner, SendInvite, Activate];
}
