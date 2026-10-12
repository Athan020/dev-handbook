// The question: the first number is n, followed by n amounts. Print their total and the
// largest amount, separated by a space. n is at least 1.
// #region program
using System.Globalization;

// Parse and print numbers the same way whatever the machine's regional settings.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Read all of standard input as whitespace-separated tokens, so it doesn't matter how the
// numbers are split across lines, or whether there are extra spaces or a trailing newline.
var tokens = Console.In.ReadToEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
var next = 0;
long NextLong() => long.Parse(tokens[next++]);

var n = (int)NextLong();
long total = 0;
var largest = long.MinValue;
for (var i = 0; i < n; i++)
{
    var amount = NextLong();
    total += amount;
    largest = Math.Max(largest, amount);
}

// Write output through one buffered writer: a Console.WriteLine per line is slow for large outputs.
using var output = new StreamWriter(Console.OpenStandardOutput());
output.WriteLine($"{total} {largest}");
// #endregion
