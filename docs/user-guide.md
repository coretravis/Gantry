# Gantry User Guide

Gantry is a .NET global CLI tool that provisions Ubuntu servers, installs the .NET runtime, configures nginx and systemd, provisions SSL certificates, and generates a GitHub Actions deployment workflow. Once provisioned, it handles all subsequent deploys, rollbacks, and status checks from the same tool.

---

## Requirements

- .NET 6 SDK or later
- SSH access to a target server (Ubuntu 22.04 or 24.04)
- A GitHub repository for CI/CD generation
- Docker Desktop (optional, for local sandbox testing)

---

## Installation

```sh
dotnet tool install --global Gantry
```

Verify:

```sh
gantry --version
```

To update:

```sh
dotnet tool update --global Gantry
```

To uninstall:

```sh
dotnet tool uninstall --global Gantry
```

---

## Configuration File

Gantry stores all project configuration in a `.deploy.yml` file in your project directory. The `init` command creates this file interactively. You can pass a different path with `--config`.

The file uses snake_case YAML. All fields and their defaults:

```yaml
gantry_version: "1.0.0"

server:
  host: ""               # Server IP or hostname
  port: 22               # SSH port (use 2222 for sandbox)
  ssh_user: root         # User for initial provisioning
  ssh_key_path: ~/.ssh/id_ed25519
  deploy_user: deployer  # User created for deployments
  deploy_key_path: ~/.ssh/gantry_deploy_ed25519
  timezone: UTC
  os_version: ""         # Detected and written by gantry init/provision - do not edit

app:
  name: ""               # Lowercase, hyphens only
  project_path: ""       # Path to .csproj relative to repo root
  port: 5000             # Port the app listens on
  deploy_path: ""        # Defaults to /var/www/<name>/app
  health_check_path: /
  health_check_timeout_seconds: 30
  releases_to_keep: 3

runtime:
  ecosystem: dotnet
  version: "8.0"

web_server:
  type: nginx

domain:
  name: ""               # Leave blank to use IP only
  www: true
  ssl: true
  ssl_email: ""

ci:
  platform: github_actions
  branch: main
  workflow_path: .github/workflows/deploy.yml
  run_tests: true

environment:
  ASPNETCORE_ENVIRONMENT: Production

plugins:          # Added automatically by gantry plugin add. Do not edit by hand.
  postgres:
    enabled: true
    version: "16"
    database: myapp_db           # Defaults to <app_name>_db
    user: myapp_user             # Defaults to <app_name>_user
    # connection_string_key: ConnectionStrings__DefaultConnection
    # run_migrations: "true"
    # migration_command: "dotnet myapp.dll migrate"
```

The `environment` block is for non-sensitive values only. It is written directly into the systemd unit file and committed to `.deploy.yml`. Do not put connection strings or API keys here.

For secrets, use `gantry env set` instead -- see the [env](#env) command.

---

## Global Options

These options apply to every command.

| Option            | Default       | Description                              |
|-------------------|---------------|------------------------------------------|
| `--config <path>` | `.deploy.yml` | Path to the configuration file           |
| `--dry-run`       | false         | Simulate all operations without changes  |
| `--verbose`       | false         | Enable debug-level console logging       |
| `--no-color`      | false         | Disable ANSI color output                |

---

## Commands

### init

Full interactive provisioning wizard. Gathers configuration, validates it, provisions the server, and generates the CI/CD workflow in one pass.

```sh
gantry init [options]
```

| Option                  | Description                                    |
|-------------------------|------------------------------------------------|
| `--skip-phases <names>` | Space-separated list of phase names to skip    |

#### What it does, in order

1. Prompts for all configuration values (with defaults if a `.deploy.yml` already exists)
2. Validates the configuration
3. Generates a deploy SSH key at `deploy_key_path` if one does not exist
4. Runs the provisioning pipeline:
   - `connect-and-verify` -- connects and checks OS, memory, disk
   - `os-hardening` -- updates packages, creates deploy user, hardens SSH, configures UFW
   - `runtime-installation` -- installs the .NET runtime
   - `web-server-configuration` -- installs nginx and writes the site config
   - `process-manager-setup` -- creates a systemd unit file
   - `ssl-provisioning` -- runs Certbot (skipped if no domain is configured)
   - `ci-generation` -- writes the GitHub Actions workflow file
   - `config-persistence` -- saves `.deploy.yml`

#### After init completes

Add these secrets to your GitHub repository (Settings > Secrets > Actions):

- `DO_HOST` -- server IP or hostname
- `DO_SSH_KEY` -- contents of the generated deploy private key

The generated workflow file is saved to `.github/workflows/deploy.yml` by default.

---

### provision

Re-runs the provisioning pipeline against an existing server using a saved `.deploy.yml`. Useful after a configuration change or a failed first run.

```sh
gantry provision [options]
```

| Option                  | Description                                    |
|-------------------------|------------------------------------------------|
| `--phase <name>`        | Run only this single phase                     |
| `--skip-phases <names>` | Space-separated list of phase names to skip    |

`connect-and-verify` always runs before the target phase so the SSH connection is established.

#### Example: re-run only SSL provisioning

```sh
gantry provision --phase ssl-provisioning
```

---

### deploy

Builds the application locally, transfers the output to the server, restarts the systemd service, and runs a health check.

```sh
gantry deploy [options]
```

A timestamped snapshot of the current deployment is saved to `/var/www/<app>/releases/` before each deploy. The number of releases retained is controlled by `releases_to_keep` in the configuration.

---

### rollback

Rolls back to a previous release. Without `--release`, presents an interactive list of the available releases.

```sh
gantry rollback [options]
```

| Option            | Description                                    |
|-------------------|------------------------------------------------|
| `--release <id>`  | Release ID to restore (e.g. `20240915-143022`) |

---

### status

Connects to the server and runs a structured health check across all components. Output includes a summary banner and a per-component table showing pass/fail/warning for the application process, port, nginx, SSL certificate, DNS resolution, and disk space. Each failure includes a remediation hint with the exact command to fix it. Recent systemd log lines are shown at the end with errors and warnings highlighted.

```sh
gantry status [options]
```

Exit codes:

| Code | Meaning                                                  |
|------|----------------------------------------------------------|
| `0`  | All checks healthy                                       |
| `1`  | One or more critical failures                            |
| `2`  | No critical failures but one or more warnings (degraded) |

---

### ci

Regenerates the CI/CD workflow file from the current configuration without re-provisioning the server.

```sh
gantry ci [options]
```

Use this after changing the branch, project path, or test settings in `.deploy.yml`.

---

### env

Manage server-side secrets and environment variables. These are stored in `/var/www/<app>/.env` on the server with `chmod 600` and are loaded by the systemd service at startup. The file is never written to locally and never committed to git.

ASP.NET Core maps environment variable names to configuration hierarchy using `__` (double underscore) as the separator. So `ConnectionStrings__DefaultConnection` sets `ConnectionStrings:DefaultConnection` in your app configuration.

```sh
gantry env set <key> <value>
gantry env set-many KEY=VALUE KEY=VALUE ...
gantry env list
gantry env unset <key>
gantry env unset-many KEY KEY ...
```

#### Examples

```sh
gantry env set ConnectionStrings__DefaultConnection "Server=...;Database=...;Password=..."
gantry env set ApiKeys__Stripe sk_live_abc123

gantry env set-many \
  ConnectionStrings__DefaultConnection="Server=...;Password=..." \
  ApiKeys__Stripe=sk_live_abc123

gantry env list

gantry env unset ApiKeys__Stripe
gantry env unset-many ApiKeys__Stripe ApiKeys__SendGrid SomeOtherKey
```

`set` and `unset` restart the service once each. `set-many` and `unset-many` apply all changes in a single write and restart the service only once at the end -- use these when configuring a new server or making several changes at once.

| Subcommand               | Description                                              |
|--------------------------|----------------------------------------------------------|
| `set <key> <value>`      | Set or update one secret, then restart                   |
| `set-many KEY=VALUE ...` | Set multiple secrets in one operation, restart once      |
| `list`                   | Show all secrets stored on the server                    |
| `unset <key>`            | Remove one secret, then restart                          |
| `unset-many KEY ...`     | Remove multiple secrets in one operation, restart once   |

The `--config` global option applies here the same as any other command.

---

### plugin

Manage server-side plugins. Plugins install and configure additional infrastructure components on the server. They are never prompted during `gantry init` -- add them explicitly after provisioning.

```sh
gantry plugin <subcommand>
```

#### plugin list

Shows all available plugins and their status for the current project.

```sh
gantry plugin list
```

#### plugin add

Connects to the server, installs the plugin, and saves it to `.deploy.yml`. The connection string or other generated secrets are written directly to the server's `.env` file and never stored locally.

```sh
gantry plugin add <name> [--set key=value ...]
```

Pass `--set` one or more times to override plugin defaults. Omitting `--set` entirely installs the plugin with all defaults applied.

```sh
gantry plugin add postgres
gantry plugin add postgres --set connection_string_key=ConnectionStrings__AppDb
gantry plugin add postgres --set database=myapp --set version=15
```

If the server has less RAM than the plugin recommends, a warning is shown before provisioning continues.

#### plugin remove

Uninstalls the plugin from the server and removes it from `.deploy.yml`. All plugin-managed resources (databases, users) are dropped.

```sh
gantry plugin remove <name>
```

#### Available plugins

| Plugin     | Description                    |
|------------|--------------------------------|
| `postgres` | PostgreSQL relational database |

#### postgres plugin options

All options are optional. Pass any of them via `--set` to override the default.

| Key                     | Default                                | Description                                       |
|-------------------------|----------------------------------------|---------------------------------------------------|
| `version`               | `16`                                   | PostgreSQL major version to install               |
| `database`              | `<app_name>_db`                        | Database name to create                           |
| `user`                  | `<app_name>_user`                      | Database user to create                           |
| `connection_string_key` | `ConnectionStrings__DefaultConnection` | Key written to the server `.env` file             |
| `run_migrations`        | (unset)                                | Set to `"true"` to run migrations on each deploy  |
| `migration_command`     | (unset)                                | Command to run for migrations                     |

---

### config

Inspect and validate the configuration file.

```sh
gantry config <subcommand> [options]
```

| Subcommand  | Description                                          |
|-------------|------------------------------------------------------|
| `show`      | Print the current configuration as a formatted table |
| `validate`  | Validate the configuration and report any errors     |

---

### sandbox

Manages a local Docker container for end-to-end testing without a cloud server. Requires Docker Desktop to be installed and running.

```sh
gantry sandbox <subcommand> [options]
```

| Option          | Default          | Description    |
|-----------------|------------------|----------------|
| `--name <name>` | `gantry-sandbox` | Container name |

#### sandbox up

Starts an Ubuntu container with SSH configured, prints the connection details, and exits. The SSH key pair is generated once at `~/.gantry/sandbox/id_ed25519` and reused on subsequent runs.

```sh
gantry sandbox up [options]
```

| Option               | Default | Description                            |
|----------------------|---------|----------------------------------------|
| `--port <port>`      | `2222`  | Host port to map to container SSH (22) |
| `--ubuntu <version>` | `24.04` | Ubuntu version (22.04 or 24.04)        |

#### sandbox down

Stops and removes the container. Does not delete the SSH key.

```sh
gantry sandbox down [options]
```

#### sandbox status

Shows the container state and connection details.

```sh
gantry sandbox status [options]
```

---

## Local Testing Workflow

This is the recommended flow for testing Gantry locally before using it against a real server.

### Step 1: Start the sandbox

```sh
gantry sandbox up
```

Gantry prints the connection details when the container is ready:

```text
Host:         localhost
SSH port:     2222
User:         root
SSH key:      C:\Users\you\.gantry\sandbox\id_ed25519
```

### Step 2: Run init

```sh
gantry init
```

Enter the sandbox connection details when prompted. Use the sandbox key printed above for both the SSH key path and the deploy key path.

### Step 3: Provision

Provisioning runs automatically at the end of `init`. To re-run it separately:

```sh
gantry provision
```

### Step 4: Deploy

```sh
gantry deploy
```

### Step 5: Check status

```sh
gantry status
```

### Step 6: Test rollback

```sh
gantry rollback
```

### Step 7: Inspect the generated workflow

The file at `.github/workflows/deploy.yml` is what GitHub Actions will run in production. Review it to confirm the app name, branch, secrets references, and health check URL are correct.

### Step 8: Clean up

```sh
gantry sandbox down
```

---

## Dry Run Mode

Any command accepts `--dry-run`. In this mode, Gantry skips all SSH connections, logs the commands it would have run, and still generates output files (the CI workflow, config file) so you can review them.

```sh
gantry init --dry-run
gantry provision --dry-run
gantry deploy --dry-run
```

Dry run is useful for reviewing the generated workflow and configuration before committing to any server changes.

---

## Logs

Gantry writes a rolling log file to `~/.gantry/logs/`. Each file covers one day and is retained for seven days. Pass `--verbose` to include debug output in both the console and the log file.

---

## Provisioning Phases

| Phase                      | Order | Description                                             |
|----------------------------|-------|---------------------------------------------------------|
| `connect-and-verify`       | 10    | SSH connection, OS and resource check                   |
| `os-hardening`             | 20    | OS updates, deploy user, SSH hardening, UFW, fail2ban   |
| `postgres-install`         | 25    | PostgreSQL install and service setup (postgres plugin)  |
| `runtime-installation`     | 30    | .NET runtime via package feed                           |
| `postgres-configure`       | 35    | Database, user, memory tuning, write connection string  |
| `web-server-configuration` | 40    | nginx install and site configuration                    |
| `process-manager-setup`    | 50    | systemd unit file and sudoers entry                     |
| `ssl-provisioning`         | 60    | Certbot with nginx plugin (skipped if no domain)        |
| `ci-generation`            | 70    | GitHub Actions workflow file                            |
| `config-persistence`       | 80    | Saves `.deploy.yml`                                     |

Plugin phases (orders 25, 35) only run when the corresponding plugin is enabled. They are skipped silently otherwise.

If a required phase fails, Gantry rolls back all completed phases in reverse order before exiting. Optional phases (ssl-provisioning and all plugin phases) record a warning and allow the pipeline to continue.

---

## Troubleshooting

### SSH connection refused

Confirm the host, port, and key path in `.deploy.yml`. Run `gantry status` with `--verbose` to see the full connection error. For the sandbox, ensure Docker Desktop is running.

### Permission denied (publickey) on deploy or env commands

The deploy key may not be installed in the deploy user's `authorized_keys`. Re-run `gantry provision --phase os-hardening` to reinstall it.

### Service did not start after deploy

Run `gantry status` to view the systemd log output. The most common causes are a missing environment variable or a port conflict. Use `gantry env list` to verify secrets are set.

### dotnet publish fails

Ensure the `project_path` in `.deploy.yml` points to the correct `.csproj` file relative to the repository root, and that the .NET SDK version on your local machine matches the `runtime.version` in the configuration.

### SSL provisioning fails

Certbot requires the domain's DNS A record to resolve to the server's IP before running. Confirm DNS propagation with `nslookup <domain>` before running `gantry provision --phase ssl-provisioning`.

### Generated workflow file looks wrong

Run `gantry ci` to regenerate it after correcting `.deploy.yml`, or use `gantry config validate` to check for configuration errors first.

### PostgreSQL plugin fails to install

The `postgres-install` phase requires apt access. If it times out, confirm the server has internet access and that UFW is not blocking outbound traffic. Re-run with `gantry plugin add postgres` -- the phase is idempotent.

### Application cannot connect to the database after plugin add

Run `gantry env list` and confirm `ConnectionStrings__DefaultConnection` is present. If it is missing, re-run `gantry plugin add postgres` to regenerate it. The password is preserved across re-runs if the user already exists.

### PostgreSQL migration hook does not run

Migrations only run when both `run_migrations: "true"` and `migration_command` are set in the `plugins.postgres` block of `.deploy.yml`. Run `gantry config show` to verify both keys are present.
