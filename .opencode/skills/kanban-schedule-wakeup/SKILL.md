---
name: kanban-schedule-wakeup
description: Use whenever a kanban Build or Continue Agent needs a timer, delayed continuation, later check, or wait for a command, page refresh, deployment, remote job, service status, or other external state. Also use when the user says 定时任务, 定时唤醒, 稍后继续/检查, 过一会再看, timer, schedule, remind, wait, poll later, or retry later, even if they do not name this skill. In a kanban card, interpret ambiguous timer/scheduled-task wording as this extension's one-time wake-up unless the user explicitly asks for an OS scheduler such as Windows Task Scheduler or cron.
---

# Kanban Schedule Wake-up

Use this skill only in a kanban Build or Continue turn. It schedules this card to resume once after a delay; it does not create an operating-system task.

## When to use it

- If the user asks this kanban Agent to set a timer, 定时任务, 定时唤醒, wait and continue, check later, retry later, or remind itself, load this skill before choosing an implementation. Do not substitute Windows Task Scheduler, `schtasks`, cron, a background shell, or a new scheduler implementation unless the user explicitly requests an OS-level persistent schedule.
- Use it proactively after completing all useful work available now when the only remaining blocker is elapsed time or changing external state, such as a long-running command, page refresh, deployment, remote job, rate-limit cooldown, or service status.
- If one check shows that the external state is still not ready and no other useful work remains, schedule another one-time wake-up rather than sleep, busy-poll, or stop without arranging continuation.
- Before scheduling, preserve the exact state needed to resume in `summary`: what is running, its identifier, what to check once, and what to do for ready versus not-ready results.

Do not use this skill in Plan or Review turns. Do not use it when work can continue immediately, when user input or permission is required, or as a substitute for completing available work. Do not run `bash sleep`, equivalent delay commands, busy polling, or repeated status loops to wait.

## Required Output

When a wake-up is necessary, your final assistant response must be exactly this single JSON object as plain text:

{
  "type": "kanban.schedule_wakeup",
  "delaySeconds": 30,
  "summary": "Wait for the page refresh to complete."
}

Do not wrap the object in a Markdown code fence. Do not include a preface, explanation, reasoning, worklog, or trailing text. After outputting the object, immediately end the turn and make no further tool calls.

## Rules

- The object must contain exactly the keys `type`, `delaySeconds`, and `summary`.
- `type` must be exactly `kanban.schedule_wakeup`.
- `delaySeconds` must be a finite integer from 5 through 86400 seconds, inclusive.
- `summary` must remain non-empty after trimming and must be at most 280 characters.
- Describe the state being awaited in `summary` so the continued turn can resume with useful context.
- Request one wake-up only. Recurring schedules, absolute times, and multiple tasks are unsupported.
- Any Markdown fence, surrounding text, extra key, invalid value, Plan output, or Review output makes the request invalid.
