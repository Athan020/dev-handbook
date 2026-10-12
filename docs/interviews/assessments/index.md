---
title: Online assessments
track: shared
status: written
---

# Online assessments

An invitation arrives: "Please complete our online assessment within seven days. It takes 90 minutes and has three questions." There's no interviewer, no one to explain your thinking to, and no hints. A program runs your code against tests you can't see, and a score goes to the hiring team.

Online assessments (OAs) are often the first technical gate, and they reward different skills from a live interview. This section covers those skills, not algorithms: there are good resources for those already, and the [coding page](./question-types/coding) links to them.

## Stop and think

::: think You'd do well in a live coding interview. What could still go wrong in an online assessment?

Almost everything that a live interviewer would have forgiven or helped with:

- **Only output counts.** A clear explanation and a nearly-working solution earn nothing if the tests fail. An interviewer would have given credit for the reasoning.
- **Hidden tests.** You see a couple of examples; you're scored on many more, including edge cases and large inputs you never see.
- **The clock.** It doesn't pause while you think, and it usually covers several questions, so time spent on one is taken from another.
- **The environment.** A browser editor, perhaps without the autocompletion you rely on, a language version you didn't choose, and input that arrives in a format you have to parse.
- **Nobody to ask.** An ambiguous statement has to be resolved by reading it carefully, not by asking.

:::

::: think Two candidates each solve two of three questions. Can their scores be very different?

Yes. Most platforms score each question by the tests it passes, and tests are often weighted. A solution that's correct but slow passes the small tests and fails the large ones. A solution with an overflow bug fails a few edge cases. A third question, attempted with a simple brute force, might still earn a third of its points. How you spend the last twenty minutes can matter as much as how good your best solution is: see [test-day strategy](./test-day-strategy).

:::

## How assessments are scored

The details depend on the platform and on how the employer has configured the test, so read the invitation and the platform's own help pages. The common shape:

- Each coding question has a few **sample tests**, shown with their expected output, and more **hidden tests**. Samples are often worth little or nothing; the hidden tests carry the score.
- Each test passes or fails on its own, and a question's score is the sum of the tests it passes. That makes **partial credit** normal: a solution that handles most cases earns most of the points.
- Hidden tests usually include **large inputs** with a time limit, so a correct but slow solution loses those points. Some platforms report this separately as performance.
- Some tests are of **other kinds**: SQL, multiple choice, debugging, or a small project scored by its own test suite. See [question types](./question-types/).

## The platforms, from a candidate's point of view

Three platforms appear often. Each employer chooses the questions, languages, duration and rules, so treat this as orientation, not a description of your test.

| Platform | Worth knowing | Practise with | Official candidate help |
|---|---|---|---|
| HackerRank | Scores each question by the hidden tests it passes. Code you typed but never ran or submitted isn't evaluated when time runs out. | The sample test linked from your invitation | [Candidate knowledge base](https://candidatesupport.hackerrank.com/), including [hidden test cases](https://candidatesupport.hackerrank.com/articles/6052652546-hidden-test-cases) and [what's evaluated at time-out](https://candidatesupport.hackerrank.com/articles/3576626591-evaluation-of-a-question-during-test-time-out) |
| Codility | Code must compile to be evaluated. Tests check correctness, including corner cases, and scalability as inputs grow. | [Codility's lessons and challenges](https://app.codility.com/programmers/) | [Candidate FAQ](https://support.codility.com/hc/en-us/sections/360008207593-FAQ-Candidates) and [how solutions are assessed](https://support.codility.com/hc/en-us/articles/360046335814-How-will-my-solutions-be-assessed) |
| CodeSignal | Besides company-specific tests, offers standardised assessments built on its General Coding Framework, which produce a single coding score. | [Its practice area](https://support.codesignal.com/hc/en-us/articles/21025134150423-How-do-I-practice-coding-questions-on-CodeSignal) | [Help centre](https://support.codesignal.com/hc/en-us) and the [general coding assessment framework](https://codesignal.com/resource/general-coding-assessment-framework/) |

## Reading the invitation

The invitation tells you most of what you need. Read it before the day, and note:

- **Duration, and when the clock starts.** Usually when you open the test, not when the invitation arrives. Once started, it rarely pauses.
- **The deadline.** Plan to start well before it, at a time of day when you're sharp.
- **Number and types of questions.** Coding only, or SQL, multiple choice, a project task?
- **Languages.** Which are allowed, and whether your preferred one is among them. The platform's documentation lists the versions it runs.
- **Rules.** Proctoring (webcam, screen recording, tab switching), whether you may use documentation or your own editor, and anything that's explicitly forbidden. Follow them: breaking them can disqualify an otherwise good result.
- **A sample or practice test.** If there's one, take it: see [before the test](./before-the-test).
- **Adjustments.** If you need extra time or other adjustments, ask the employer before you start, not after.

::: think The invitation says "You may use any online resources". Does that change how you prepare?

A little. You can look up an API you've forgotten, so you needn't memorise every method signature. But the time limit still applies, and searching for an algorithm you don't know costs far more time than recognising one you do. Prepare as if you'll have no help, and treat looking things up as a safety net. Check, too, whether "resources" includes AI assistants: many tests forbid them even when they allow documentation.

:::

## In this section

- [Before the test](./before-the-test): practise in the platform's editor, set up your language, check your environment.
- [Test-day strategy](./test-day-strategy): spend time where the points are, bank partial credit, know when to move on.
- [Reading constraints](./reading-constraints): work out from the input size how fast a solution must be.
- [Question types](./question-types/): coding, SQL, project tasks, code review and debugging, multiple choice.
- [C# toolkit](./csharp-toolkit): the parts of C# and .NET that assessments lean on.
- [Practice sets](./practice/): timed sets with visible and hidden tests, to run on your own machine.
