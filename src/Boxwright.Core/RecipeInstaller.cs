namespace Boxwright.Core;

/// <summary>
/// Prepares an unattended installer ISO from a declarative <see cref="UnattendedRecipe"/> (ADR-0026),
/// instead of a hardcoded per-family installer. The generic engine that lets the community add a distro's
/// unattended install as data. Implemented by <see cref="RecipeInstaller"/>.
/// </summary>
public interface IRecipeInstaller
{
    /// <summary>
    /// Prepares <paramref name="isoPath"/> in <paramref name="vmFolderPath"/> per <paramref name="recipe"/>
    /// (extract kernel/initrd, inject the seed) and returns the boot plan.
    /// </summary>
    /// <exception cref="InstallMediaException">The recipe kind is unsupported, or the ISO lacks the recipe's kernel/initrd.</exception>
    UnattendedInstallPlan Prepare(UnattendedRecipe recipe, string isoPath, string vmFolderPath, UnattendedAnswers answers);
}

/// <summary>
/// The default <see cref="IRecipeInstaller"/>. Supports the <c>initrd-inject</c> mechanism
/// (preseed/kickstart style): copy the recipe's kernel + initrd out of the ISO, inject the templated seed
/// file into the initrd (<see cref="InitrdFileInjector"/>), and boot with the templated kernel command
/// line. Two kinds: <c>initrd-inject</c> (preseed/kickstart injected into the initrd) and <c>cloud-init</c>
/// (the seed written as a NoCloud CIDATA disk — Ubuntu autoinstall style).
/// </summary>
public sealed class RecipeInstaller : IRecipeInstaller
{
    private const string KernelFileName = "vmlinuz";
    private const string InitrdFileName = "initrd";

    /// <inheritdoc />
    public UnattendedInstallPlan Prepare(UnattendedRecipe recipe, string isoPath, string vmFolderPath, UnattendedAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vmFolderPath);
        ArgumentNullException.ThrowIfNull(answers);

        bool isInject = string.Equals(recipe.Kind, UnattendedRecipe.KindInitrdInject, StringComparison.OrdinalIgnoreCase);
        bool isCloudInit = string.Equals(recipe.Kind, UnattendedRecipe.KindCloudInit, StringComparison.OrdinalIgnoreCase);
        if (!isInject && !isCloudInit)
        {
            throw new InstallMediaException(
                $"Unsupported unattended recipe kind '{recipe.Kind}'. Supported: '{UnattendedRecipe.KindInitrdInject}', '{UnattendedRecipe.KindCloudInit}'.");
        }

        if (string.IsNullOrWhiteSpace(recipe.KernelPath))
        {
            throw new InstallMediaException("An unattended recipe needs a kernelPath.");
        }

        if (isInject && string.IsNullOrWhiteSpace(recipe.SeedFileName))
        {
            throw new InstallMediaException("An initrd-inject recipe needs a seedFileName.");
        }

        Directory.CreateDirectory(vmFolderPath);

        using IsoMedia iso = IsoMedia.Open(isoPath);
        if (!iso.FileExists(recipe.KernelPath))
        {
            throw new InstallMediaException($"The ISO has no kernel at '{recipe.KernelPath}' (recipe kernelPath).");
        }

        iso.CopyFile(recipe.KernelPath, Path.Combine(vmFolderPath, KernelFileName));

        string initrdSource = recipe.InitrdPaths.FirstOrDefault(iso.FileExists)
            ?? throw new InstallMediaException(
                $"The ISO has no initrd at any of the recipe's initrdPaths: {string.Join(", ", recipe.InitrdPaths)}.");
        string initrdDest = Path.Combine(vmFolderPath, InitrdFileName);
        iso.CopyFile(initrdSource, initrdDest);

        // One password hash per install, so a template that references it more than once stays consistent.
        string passwordHash = Sha512Crypt.Hash(answers.Password);
        string seed = RecipeTemplate.Substitute(recipe.SeedTemplate, answers, passwordHash, iso.VolumeLabel);

        IReadOnlyList<DiskConfig> seedDisks;
        if (isInject)
        {
            // Preseed/kickstart: the seed file rides inside the initrd; no extra disk.
            InitrdFileInjector.Append(initrdDest, recipe.SeedFileName, seed);
            seedDisks = [];
        }
        else
        {
            // cloud-init: the seed is a NoCloud CIDATA disk the installer probes for (the `append` must
            // carry the matching ds=nocloud arg — the recipe author supplies it).
            CloudInitSeedGenerator.WriteSeed(vmFolderPath, seed, Guid.NewGuid().ToString());
            seedDisks = [new DiskConfig { File = CloudInitSeedGenerator.SeedFileName, Format = "raw", Interface = "virtio" }];
        }

        return new UnattendedInstallPlan
        {
            Boot = new InstallBootConfig
            {
                KernelFile = KernelFileName,
                InitrdFile = InitrdFileName,
                Append = RecipeTemplate.Substitute(recipe.Append, answers, passwordHash, iso.VolumeLabel),
            },
            SeedDisks = seedDisks,
        };
    }
}
