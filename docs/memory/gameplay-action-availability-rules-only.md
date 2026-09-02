# Gameplay action availability uses rules only

- Gameplay availability has one extension mechanism: explicit ordered rule collections.
- Do not add a virtual or specialized availability hook to `GameplayAction`; it creates a hidden
  second pipeline that authoring, diagnostics, and presentation cannot inspect consistently.
- Integrations compose explicit collections when they need layers. Interaction evaluates
  `TargetRules` first and `Action.Rules` second; both remain ordinary visible rule arrays.
- Access validation is a separate trust-boundary gate and must not be represented as availability.
