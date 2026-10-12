using System.Diagnostics;

namespace Assessments.Toolkit;

// Runs the standard-input template as a real process, the way a platform does.
public class StdinProgramTests
{
    [Theory]
    [InlineData("3\n10 -5 20\n", "25 20")]
    [InlineData("3\n10\n  -5\n20", "25 20")]                          // split across lines, no trailing newline
    [InlineData("2\r\n3000000000 3000000000\r\n", "6000000000 3000000000")] // Windows line endings, beyond int
    public async Task Prints_the_total_and_the_largest(string input, string expected)
    {
        var start = new ProcessStartInfo("dotnet", Path.Combine(AppContext.BaseDirectory, "Stdin.dll"))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, output.TrimEnd());
    }
}
