namespace Assessments.Set2;

// Input for problem 3: a customer is with a teller from Start (inclusive) to End (exclusive).
public readonly record struct Visit(long Start, long End);

// Input for problem 2: an inclusive range of day indexes.
public readonly record struct DayRange(int From, int To);
