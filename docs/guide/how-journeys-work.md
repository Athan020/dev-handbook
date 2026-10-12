---
title: How journeys work
track: shared
status: written
---

# How journeys work

Knowing how to build something is different from knowing how it behaves under pressure. You can write a clean REST API and still come up short when an interviewer asks what happens at fifty times the traffic. Reference material rarely closes that gap, because it explains *what* things are, not *when* to reach for them, what they break, or what comes next.

This handbook is organised as journeys to close that gap.

## What a journey is

Each journey:

- **Starts from something you already own.** For the backend track, that's a working REST API for a banking product, backed by a relational database. For the frontend track, it's a working page that calls an API.
- **Applies one kind of pressure.** More traffic, slow work inside requests, failures, more teams, attackers.
- **Moves outward one stage at a time.** Each stage fixes the problem in front of you, and that fix creates the problem that starts the next stage.
- **Ends with a capstone.** A realistic interview question, answered with everything the journey built up.

## Stages and triggers

Every stage starts from a **trigger**: a symptom you'd actually see, such as a slow endpoint, a queue that keeps growing or a customer who got two notifications. In [B1: Your API gets popular](/backend/journeys/your-api-gets-popular/), the chain starts like this:

1. "The transactions endpoint is slow at peak" leads to **measuring first**.
2. Measuring shows the query plan doing a full scan, which leads to **indexes and query shape**.
3. The query is now indexed, but each request still runs hundreds of queries, which leads to **what the ORM is doing**.

The order matters. Each stage teaches the cheaper fix before the expensive one, so you learn to diagnose before you reach for read replicas or sharding. In an interview, that order is often what separates a strong answer from a list of buzzwords.

Every stage page has the same layout. [How a stage page works](./module-format) walks through it.

## The capstone

The capstone is the question a journey prepares you for, such as "Reads are degrading on a 10M-row table. What do you do?" It follows the shape of a system design interview: clarify the problem, estimate the numbers, propose a design, then go deep where the interviewer pushes. Each decision links back to the stage that covers it.

## Two ways through

**To grow in your role**, take a journey in order. Answer each stop-and-think question before reading on, and do the labs: they run against a sample banking API on your own machine.

**To prepare for an interview soon**, start with the capstones. Try to answer each one out loud before reading the model answer. Wherever you can't justify a decision, follow the link back to the stage that covers it and work through that stage. Then look at [online assessments](/interviews/assessments/) if the company uses one.

Whichever path you take, check the [core self-check](/backend/core-self-check) first. The journeys assume you already know some fundamentals, such as HTTP, basic SQL and async code. The self-check tells you quickly whether you do, and links to good resources if not.

## What the handbook doesn't do

It doesn't re-teach fundamentals that good resources already cover well, and it isn't a bank of algorithm drills. For those, the self-checks and the [coding patterns](/interviews/coding-patterns) page give you a short check and link out. The handbook spends its words on judgement: which option to reach for, what it breaks, and what to do next.
