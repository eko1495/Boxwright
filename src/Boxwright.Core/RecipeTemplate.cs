namespace Boxwright.Core;

/// <summary>
/// Fills an unattended-recipe template (ADR-0026) with the install answers: a pure, order-independent
/// replacement of <c>{username}</c>, <c>{password}</c>, <c>{passwordHash}</c>, <c>{hostname}</c>,
/// <c>{locale}</c>, <c>{timezone}</c>, <c>{keyboard}</c>, and <c>{isoLabel}</c>. The password hash is
/// passed in (computed once per install) so a template that uses it twice stays consistent.
/// </summary>
public static class RecipeTemplate
{
    /// <summary>Substitutes the recipe placeholders in <paramref name="template"/>.</summary>
    public static string Substitute(string template, UnattendedAnswers answers, string passwordHash, string isoLabel)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(passwordHash);

        return template
            .Replace("{username}", answers.Username, StringComparison.Ordinal)
            .Replace("{password}", answers.Password, StringComparison.Ordinal)
            .Replace("{passwordHash}", passwordHash, StringComparison.Ordinal)
            .Replace("{hostname}", answers.Hostname, StringComparison.Ordinal)
            .Replace("{locale}", answers.Locale, StringComparison.Ordinal)
            .Replace("{timezone}", answers.Timezone, StringComparison.Ordinal)
            .Replace("{keyboard}", answers.KeyboardLayout, StringComparison.Ordinal)
            .Replace("{isoLabel}", isoLabel ?? string.Empty, StringComparison.Ordinal);
    }
}
