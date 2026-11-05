using PersonalHub.Web.Features.Students;

namespace PersonalHub.UnitTests.Features.Students;

public class StudentFieldsTests
{
    [Fact]
    public void Validate_NormalizesValidInput()
    {
        var errors = StudentFields.Validate("  Mariana   Lopes ", " Mariana@Example.TEST ", "(11) 98765-4321", "1.234,50", "31", out var data);

        Assert.Empty(errors);
        Assert.Equal(new StudentData("Mariana Lopes", "mariana@example.test", "+5511987654321", 1234.50m, 31), data);
    }

    [Fact]
    public void Validate_ReportsEveryInvalidField()
    {
        var errors = StudentFields.Validate(" ", "sem-arroba", "123", "0", "32", out _);

        Assert.Equal(["BillingDueDay", "Email", "LessonPrice", "Name", "Phone"], errors.Keys.Order());
    }

    [Fact]
    public void Validate_RejectsNameLongerThanTheColumn()
    {
        var errors = StudentFields.Validate(new string('a', StudentFields.NameMaxLength + 1), "a@b.test", "11987654321", "80", "10", out _);

        Assert.Equal("Use no máximo 150 caracteres.", errors["Name"]);
    }
}
