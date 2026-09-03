# Golden lessons

Each JSON file in this folder is a hand-authored "ideal" lesson expressed in the
structured `BoardAction` schema. These serve two purposes:

1. **Reference examples** for the Director agent (few-shot prompting).
2. **Regression fixtures** — render them in `/BoardDebug` and diff against
   generated output.

## File shape

```jsonc
{
  "title": "Pythagorean theorem",
  "subject": "Math",
  "grade": "9-10",
  "learningGoal": "Given the two legs of a right triangle, find the hypotenuse.",
  "spokenLines": ["...", "..."],
  "timings":     [0.0, 3.4, ...],
  "actions": [
    { "type": "write_text", "id": "t1", "region": "Title", "text": "Pythagorean theorem",
      "style": { "size": "title", "emphasis": true } }
    // ...
  ]
}
```

Invariants: `actions.length === spokenLines.length === timings.length`.
