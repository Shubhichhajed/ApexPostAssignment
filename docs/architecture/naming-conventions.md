# Project and Test Naming Conventions

This repository keeps the existing solution and project names unchanged so current build scripts, documentation, and project references continue to work.

- **Solution:** `ApexPost.sln`
- **Production projects:** `ApexPost.Domain`, `ApexPost.Application`, `ApexPost.Infrastructure`, and `ApexPost.Web`
- **Test projects:** `ApexPost.UnitTests` and `ApexPost.IntegrationTests`
- **Project files:** Match the project name (for example, `ApexPost.UnitTests.csproj`).
- **Namespaces:** Follow the project name and folder structure (for example, `ApexPost.UnitTests.Domain`).
- **Test files and classes:** Use descriptive PascalCase names ending in `Tests`.
- **Test methods:** Use descriptive names with underscores to separate the scenario and expected behavior, consistent with the existing test suite.
- **Test organization:** Keep domain tests under `tests/ApexPost.UnitTests/Domain`, application tests under `tests/ApexPost.UnitTests/Application`, and integration tests under `tests/ApexPost.IntegrationTests`.

New tests should follow these conventions and should not require SQL Server unless they are explicitly integration tests.
