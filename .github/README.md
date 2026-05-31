# GitHub Configuration

This directory contains GitHub-specific configuration files for the Qobuzarr project.

## Structure

### Workflows (`.github/workflows/`)

- **`ci.yml`** - Main CI/CD pipeline for building and testing the plugin and CLI
- **`release.yml`** - Automated release workflow for creating GitHub releases with artifacts  
- **`security.yml`** - Comprehensive security scanning including CodeQL, Semgrep, secrets detection
- **`dependency-review.yml`** - Weekly dependency vulnerability and license compliance checking

### Issue Templates (`.github/ISSUE_TEMPLATE/`)

- **`bug_report.yml`** - Structured bug report template with Lidarr/Qobuz specific fields
- **`feature_request.yml`** - Feature request template with categorization and priority
- **`security_report.yml`** - Security vulnerability reporting (for non-critical issues)
- **`config.yml`** - Issue template configuration with links to documentation

### Pull Request Template

- **`PULL_REQUEST_TEMPLATE.md`** - Comprehensive PR template with quality checklists, architecture compliance, and testing requirements

### Code Review & Ownership

- **`CODEOWNERS`** - Defines code ownership and required reviewers for different parts of the codebase

### Automation & Dependencies  

- **`dependabot.yml`** - Automated dependency updates for NuGet packages, GitHub Actions, and Docker
- **`dependency-review-config.yml`** - Configuration for dependency review action (license compliance, security)

### Funding

- **`FUNDING.yml`** - Template for project sponsorship and funding options (customize as needed)

## Key Features

### Security-First Approach

- Multiple security scanning layers (CodeQL, Semgrep, TruffleHog)
- Automated vulnerability detection for dependencies
- License compliance checking
- Security-focused issue templates

### Comprehensive Quality Gates

- Multi-stage build verification
- Test coverage requirements  
- Architecture compliance checks
- Performance impact assessment

### Lidarr Plugin Specific

- Templates tailored for Lidarr plugin development
- Qobuz integration considerations
- Plugin-CLI architecture validation

### Developer Experience

- Clear, structured templates
- Automated dependency management
- Comprehensive code ownership
- Quality-focused PR reviews

## Usage

These configurations are automatically used by GitHub when:

- Creating issues (issue templates)
- Opening pull requests (PR template)  
- Code changes are pushed (workflows)
- Dependencies need updates (Dependabot)
- Security reviews are needed (dependency review)

## Customization

To customize for your fork:

1. Update `CODEOWNERS` with your GitHub username
2. Configure `FUNDING.yml` with your sponsorship details
3. Adjust workflow schedules in `dependabot.yml` as needed
4. Modify security scanning schedules if required

## Attribution

This GitHub configuration supports the Qobuzarr project, which builds upon [TrevTV's Lidarr.Plugin.Qobuz](https://github.com/TrevTV/Lidarr.Plugin.Qobuz).
