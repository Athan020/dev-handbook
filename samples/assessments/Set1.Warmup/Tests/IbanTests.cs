namespace Assessments.Set1;

// The IBANs here are made up, with correct check digits.
public class IbanTests
{
    [Theory, Trait(Tests.Category, Tests.Visible)]
    [InlineData("GB85TEST12345612345678", true)]
    [InlineData("GB86TEST12345612345678", false)]
    public void Examples(string iban, bool valid) => Assert.Equal(valid, Iban.IsValid(iban));

    [Theory, Trait(Tests.Category, Tests.Hidden)]
    [InlineData("GB85 TEST 1234 5612 3456 78", true)]   // printed with spaces
    [InlineData("gb85test12345612345678", true)]        // lower case
    [InlineData("DE45120300001234567890", true)]
    [InlineData("NO7112345678903", true)]                // 15 characters, the shortest allowed
    [InlineData("LC02SAVE00000000000000000001234567", true)] // 34 characters: far too big for long or decimal
    [InlineData("LC02SAVE000000000000000000012345670", false)] // 35 characters
    [InlineData("NO711234567890", false)]                // 14 characters
    [InlineData("GB85TEST1234561234567-", false)]        // not a letter or digit
    [InlineData("1285TEST12345612345678", false)]        // country must be letters
    [InlineData("GBX5TEST12345612345678", false)]        // check digits must be digits
    [InlineData("", false)]
    public void Hidden_cases(string iban, bool valid) => Assert.Equal(valid, Iban.IsValid(iban));
}
