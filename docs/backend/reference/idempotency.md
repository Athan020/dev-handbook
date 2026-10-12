---
title: Idempotency
track: backend
sources: [02-system-design/idempotency.md]
status: written
---

# Idempotency

An operation is idempotent if doing it twice has the same effect as doing it once. In a system where messages are redelivered, requests are retried and processes crash halfway, it's what makes "try again" safe.

This page collects the idea in one place. The journeys show it under pressure: [B2 stage 3](/backend/journeys/work-outside-the-request/03-delivered-twice) for consumers that receive a message twice, and [B3 stage 3](/backend/journeys/the-money-must-add-up/03-paid-twice) for an API that receives a request twice.

## Why repeats are unavoidable

A caller that doesn't hear back can't tell "it failed" from "it worked, and the reply was lost". Its only safe choices are to give up, possibly losing the work, or to try again, possibly doing it twice. Retrying is usually right, so the receiver has to cope with repeats:

- A client retries after a timeout, or a customer taps the button again.
- A message broker redelivers a message whose acknowledgement was lost (at-least-once delivery).
- A publisher republishes after losing the broker's confirmation.
- A stream or a dead-letter queue is replayed on purpose.

Exactly-once *delivery* over an unreliable network isn't possible. An exactly-once *effect* is: at-least-once delivery, plus a receiver that makes repeats harmless.

## Naturally idempotent, and not

| Operation | Idempotent? | Why |
|---|---|---|
| `PUT /accounts/42/nickname` | Yes | Sets a value; setting it again changes nothing |
| `DELETE /cards/7` | Yes, in effect | The card is gone either way, though the second response may be a 404 |
| "Set the status to sent" | Yes | Same final state |
| `POST /transfers` | No | Each request creates a new transfer |
| "Add 100 points" | No | Each repeat adds 100 more |
| `UPDATE accounts SET balance = balance - 100` | No | Each repeat subtracts again |

HTTP defines `GET`, `PUT` and `DELETE` as idempotent, and `POST` and `PATCH` as not ([RFC 9110](https://www.rfc-editor.org/rfc/rfc9110#name-idempotent-methods)). That's a contract your API must honour, not something HTTP does for you.

## Making an operation idempotent

Every technique needs an **identifier that's the same on every repeat**, chosen by whoever starts the work: a message id, or an idempotency key the client generates once per intended operation and sends with every retry.

1. **Record the identifier in the same transaction as the effect.** Insert the key or message id into a table with a unique constraint, and make the change, in one transaction. A repeat finds the key and skips the change. If the record and the effect are in different places, such as a cache and a database, a crash between them breaks the guarantee.
2. **Put a unique constraint on the effect itself.** For example, at most one reward row per transfer: the repeat's insert fails, and that means "already done".
3. **Make the change conditional.** "Set the status to sent where it's pending", or "apply this event if its sequence number is the next one", so a repeat matches nothing.
4. **Pass the identifier on.** A call to a system outside your transaction, such as an email or payment provider, can't be undone by your rollback. Send it your identifier so it can deduplicate, if it supports that, and check how long it remembers.

## For APIs: idempotency keys

A client-supplied key, often in an `Idempotency-Key` header, turns a non-idempotent `POST` into a safely retryable one. The details that matter:

- **Scope the key** to the client or account, so two clients' keys can't collide.
- **Store the result** with the key, and return the same response to a repeat, rather than "already done".
- **Compare the request.** The same key with a different body is a client bug: reject it rather than returning the first result.
- **Handle concurrent repeats.** A second request can arrive while the first is still running. A unique constraint makes one of them win; the other waits or is told to retry.
- **Expire keys** after a period that's longer than any client retries for.

[B3 stage 3](/backend/journeys/the-money-must-add-up/03-paid-twice) builds this, and measures what goes wrong without each of these details.

## In the interview

"What's idempotency, and how do you implement it?" is a common question in its own right, and a common follow-up to any question about payments, messaging or retries. A good answer covers: why repeats happen, the difference between exactly-once delivery and an exactly-once effect, an identifier that's stable across repeats, recording it in the same transaction as the effect, and what to do about side effects outside your database.
