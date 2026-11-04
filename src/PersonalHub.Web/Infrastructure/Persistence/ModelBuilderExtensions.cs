using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PersonalHub.Web.Infrastructure.Persistence;

internal static class ModelBuilderExtensions
{
    // Formato E.164: "+" seguido de 8 a 15 dígitos, sem zero à esquerda.
    public const string E164Pattern = @"^\+[1-9][0-9]{7,14}$";

    /// <summary>
    /// Usa a coluna de sistema <c>xmin</c> do PostgreSQL como token de concorrência otimista,
    /// via propriedade sombra: o domínio não precisa conhecer a coluna.
    /// </summary>
    public static void HasXminConcurrencyToken<T>(this EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<uint>("xmin").IsRowVersion();
    }

    /// <summary>Grava o enum como texto e retorna o SQL de check com os valores permitidos.</summary>
    public static string StoreAsString<T, TEnum>(
        this EntityTypeBuilder<T> builder,
        System.Linq.Expressions.Expression<Func<T, TEnum>> property,
        string column,
        int maxLength)
        where T : class
        where TEnum : struct, Enum
    {
        builder.Property(property).HasConversion<string>().HasMaxLength(maxLength);

        var values = string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"));
        return $"\"{column}\" IN ({values})";
    }
}
