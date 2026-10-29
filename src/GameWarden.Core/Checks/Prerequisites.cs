namespace GameWarden.Core.Checks;

public sealed record PrerequisiteStatus(string Id, bool Installed, string Hint);

/// <summary>Prerequisites an adapter can declare (08 §2). Core checks them before a start; installing them is up to the Owner for now.</summary>
public static class Prerequisites
{
    public const string VcRedistX64 = "vcredist-x64";
    public const string VcRedistX86 = "vcredist-x86";
    public const string DirectXLegacy = "directx-legacy";

    public static readonly IReadOnlyList<string> Known = [VcRedistX64, VcRedistX86, DirectXLegacy];

    public static PrerequisiteStatus Unknown(string id) => new(id, false, $"Unknown prerequisite '{id}'.");
}

/// <summary>Whether a prerequisite of <see cref="Prerequisites.Known"/> is installed (ADR-0016); <see cref="Platform.PlatformServices"/> picks the adapter.</summary>
public interface IPrerequisiteChecker
{
    PrerequisiteStatus Check(string id);
}

/// <summary>
/// The development platform (ADR-0016): every prerequisite is a Windows runtime, so none is reported installed; an
/// adapter that declares one does not start here.
/// </summary>
public sealed class DevelopmentPrerequisiteChecker : IPrerequisiteChecker
{
    public const string NotChecked = "Checked on Windows only (ADR-0016).";

    public PrerequisiteStatus Check(string id) => Prerequisites.Known.Contains(id) ? new(id, false, NotChecked) : Prerequisites.Unknown(id);
}
