---
name: kanban-review-result
description: Use when a kanban Review Agent must produce its final review result for Alfred. Formats review findings as readable Markdown plus the required marked JSON counts block.
---

# Kanban Review Result

Use this skill only for the final output of a kanban review, or when rewriting a previous review result that violated the required final-output contract.

Your output must be readable Markdown followed by exactly one marked JSON counts block. Do not include a preface, Markdown code fence, reasoning, worklog, tool-use process, or trailing text.

The Markdown content is mandatory even though Alfred's pass/fail decision uses the JSON block. The Markdown review and conclusion are the build agent's repair instructions for the next fix pass, so they must be complete, actionable, and consistent with the JSON counts.

Hard ban: the final answer must not contain triple-backtick fences or any `text`/`json` fence label. The JSON marker lines must appear at the top level of the response, not inside a quoted block, list item, or code block.

The first line of your response must be exactly `# Review Result`.

The final line of your response must be exactly `KANBAN_REVIEW_RESULT_JSON_END`.

## Required Output

# Review Result

## Status
<approved|changes-requested>

## Severity Counts
- high: <number>
- medium: <number>
- low: <number>

## Summary
<short summary>

## Issues
- [M1] <issue title>
  - Verified evidence: <location, current behavior, impact, and relevant code relationship>
  - Recommended direction: <minimal safe repair direction; label unverified root-cause assumptions as Needs confirmation>
  - Validation: <targeted check, test, or command and expected result>

## Conclusion
<final actionable review conclusion for the build agent>

KANBAN_REVIEW_RESULT_JSON_BEGIN
{"status":"<approved|changes-requested>","high":<number>,"medium":<number>,"low":<number>}
KANBAN_REVIEW_RESULT_JSON_END

Copy the required output shape as plain text lines only. Do not wrap it in a Markdown code block.

## Rules

- The JSON block is authoritative for Alfred's automated decisions.
- The Markdown block is authoritative repair context for humans and build/address-review agents.
- The `## Conclusion` section is the concise action instruction for the next fix pass.
- Always include readable Markdown with `# Review Result`, `## Status`, `## Severity Counts`, `## Summary`, `## Issues`, and `## Conclusion`, followed by one marked JSON counts block.
- The JSON block must be a single-line JSON object with exactly these keys: `status`, `high`, `medium`, `low`.
- `status` must be exactly `approved` or `changes-requested`.
- `high`, `medium`, and `low` must be non-negative integers.
- Do not include any extra JSON keys such as `summary`, `issues`, or `conclusion`.
- The review can be `approved` only when `high` is `0` and `medium` is `0`.
- Every issue must be counted in exactly one severity bucket: high, medium, or low.
- Treat `high` and `medium` findings as repair guidance for the next Build pass, not as unquestionable commands. Give the Build agent enough evidence to begin targeted work while preserving its responsibility to check the current code and choose the safest implementation.
- Every `high` and `medium` issue must include a stable issue identifier, severity, and these labeled fields: `Verified evidence`, `Recommended direction`, and `Validation`.
- `Verified evidence` must state the evidence-backed location, current behavior, impact, and relevant code relationship. Do not present a suspected root cause as verified evidence.
- `Recommended direction` must describe the smallest safe repair direction. It is guidance, not a mandatory patch; label any assumption requiring a local check as `Needs confirmation`.
- `Validation` must name a targeted test, command, or observable expected result. Do not use vague instructions such as "add tests" or "verify the fix".
- The Markdown issue list must match the JSON severity counts. If the JSON says `high: 1`, the Markdown must contain exactly one high-severity issue.
- The conclusion must tell the build agent what to do next. For `changes-requested`, list the blocking issue identifiers in repair order, tell it to reuse verified evidence, perform only targeted checks for `Needs confirmation`, and allow it to adapt or reject a recommendation when the current code provides contrary evidence.
- Returning only `# Review Result` Markdown is invalid because it omits the required JSON block and markers.
- Returning only the JSON block is invalid because it gives Alfred a decision but gives the build agent no repair instructions.
- Returning the review inside a Markdown code fence is invalid because Alfred requires top-level JSON marker lines.

## Rewrite Mode

When rewriting a previous invalid review result:

- Do not add new findings.
- Do not remove findings.
- Do not change severity.
- Preserve the previous status, severity counts, summary, issues, and conclusion when they are present.
- If the previous review content has inconsistent counts, use the explicit Severity Counts section if present; otherwise count the listed issues by severity labels.
- If the previous content is ambiguous, choose `changes-requested` with a medium issue explaining the ambiguity instead of inventing an approval.
