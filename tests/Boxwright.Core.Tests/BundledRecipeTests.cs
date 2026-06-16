using Boxwright.Core;
using Xunit;

namespace Boxwright.Core.Tests;

// The bundled catalog ships Debian + Fedora as declarative initrd-inject recipes (ADR-0026), replacing
// the deleted DebianPreseedInstaller / FedoraKickstartInstaller. These tests assert the recipes are
// well-formed and that substituting their seed templates yields the same preseed/kickstart directives
// the bespoke C# builders used to produce (the coverage the deleted builder tests gave).
public sealed class BundledRecipeTests
{
    private static readonly UnattendedAnswers Answers = new()
    {
        Hostname = "boxwright-test",
        Username = "alice",
        Password = "s3cr3t-pw",
        Locale = "en_US.UTF-8",
        Timezone = "Europe/Warsaw",
        KeyboardLayout = "us",
    };

    private static async Task<OsCatalogEntry> EntryAsync(string id)
    {
        IReadOnlyList<OsCatalogEntry> entries = await new BundledOsCatalogSource().GetEntriesAsync();
        return entries.Single(e => e.Id == id);
    }

    // The bundled recipe seed, with placeholders filled the same way RecipeInstaller fills them.
    private static string Seed(UnattendedRecipe recipe) =>
        RecipeTemplate.Substitute(recipe.SeedTemplate, Answers, Sha512Crypt.Hash(Answers.Password), "TEST-LABEL");

    [Fact]
    public async Task Debian_netinst_carries_a_well_formed_initrd_inject_recipe()
    {
        UnattendedRecipe recipe = (await EntryAsync("debian-13-netinst")).Unattended!;

        Assert.NotNull(recipe);
        Assert.Equal(UnattendedRecipe.KindInitrdInject, recipe.Kind);
        Assert.Equal(@"install.amd\vmlinuz", recipe.KernelPath);
        Assert.Contains(@"install.amd\initrd.gz", recipe.InitrdPaths);
        Assert.Equal("preseed.cfg", recipe.SeedFileName);
        Assert.Equal("auto=true priority=critical", recipe.Append);
    }

    [Fact]
    public async Task Debian_seed_substitutes_answers_and_a_crypt_hash()
    {
        string preseed = Seed((await EntryAsync("debian-13-netinst")).Unattended!);

        Assert.Contains("d-i netcfg/get_hostname string boxwright-test", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i passwd/username string alice", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i debian-installer/locale string en_US.UTF-8", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i time/zone string Europe/Warsaw", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i keyboard-configuration/xkb-keymap select us", preseed, StringComparison.Ordinal);
        Assert.Contains("d-i passwd/user-password-crypted password $6$", preseed, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-pw", preseed, StringComparison.Ordinal); // hash only, never the plaintext
        Assert.DoesNotContain("{", preseed, StringComparison.Ordinal); // every placeholder resolved
    }

    [Theory]
    [InlineData("d-i partman/confirm boolean true")]
    [InlineData("d-i partman/confirm_nooverwrite boolean true")]
    [InlineData("d-i partman-partitioning/confirm_write_new_label boolean true")]
    [InlineData("tasksel tasksel/first multiselect standard, gnome-desktop")]
    [InlineData("d-i debian-installer/exit/poweroff boolean true")] // graduates the VM to a disk boot
    public async Task Debian_seed_automates_every_gate(string directive)
    {
        Assert.Contains(directive, Seed((await EntryAsync("debian-13-netinst")).Unattended!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fedora_netinst_carries_a_well_formed_initrd_inject_recipe()
    {
        UnattendedRecipe recipe = (await EntryAsync("fedora-44-netinst")).Unattended!;

        Assert.NotNull(recipe);
        Assert.Equal(UnattendedRecipe.KindInitrdInject, recipe.Kind);
        Assert.Equal(@"images\pxeboot\vmlinuz", recipe.KernelPath);
        Assert.Contains(@"images\pxeboot\initrd.img", recipe.InitrdPaths);
        Assert.Equal("ks.cfg", recipe.SeedFileName);
        // The kickstart selector and stage2 must match the seed file name / medium label.
        Assert.Contains("inst.ks=file:/ks.cfg", recipe.Append, StringComparison.Ordinal);
        Assert.Contains("inst.stage2=hd:LABEL={isoLabel}", recipe.Append, StringComparison.Ordinal);
        Assert.Contains("inst.text", recipe.Append, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fedora_append_substitutes_the_iso_label()
    {
        UnattendedRecipe recipe = (await EntryAsync("fedora-44-netinst")).Unattended!;

        string append = RecipeTemplate.Substitute(recipe.Append, Answers, "ignored", "Fedora-E-dvd-x86_64-44");

        Assert.Contains("inst.stage2=hd:LABEL=Fedora-E-dvd-x86_64-44", append, StringComparison.Ordinal);
        Assert.DoesNotContain("{", append, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fedora_seed_substitutes_answers_and_a_crypt_hash()
    {
        string ks = Seed((await EntryAsync("fedora-44-netinst")).Unattended!);

        Assert.Contains("network --bootproto=dhcp --hostname=boxwright-test", ks, StringComparison.Ordinal);
        Assert.Contains("lang en_US.UTF-8", ks, StringComparison.Ordinal);
        Assert.Contains("timezone Europe/Warsaw --utc", ks, StringComparison.Ordinal);
        Assert.Contains("keyboard --vckeymap=us", ks, StringComparison.Ordinal);
        Assert.Contains("user --name=alice --groups=wheel --iscrypted --password=$6$", ks, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-pw", ks, StringComparison.Ordinal);
        Assert.DoesNotContain("{", ks, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clearpart --all --initlabel")]
    [InlineData("autopart")]
    [InlineData("rootpw --lock")]
    [InlineData("@^workstation-product-environment")] // GNOME, from the mirrors (netinst ships no packages)
    [InlineData("poweroff")] // graduates the VM to a disk boot
    public async Task Fedora_seed_automates_every_step(string directive)
    {
        Assert.Contains(directive, Seed((await EntryAsync("fedora-44-netinst")).Unattended!), StringComparison.Ordinal);
    }
}
