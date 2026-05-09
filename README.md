![Build Status](https://github.com/coretravis/Gantry/actions/workflows/build.yml/badge.svg)

# Gantry

I wanted to deploy a .NET Razor Pages app as a quick demo. On Azure, the cheapest plan that includes a custom domain and SSL came to nearly $60 a month. The same thing on a DigitalOcean droplet costs $6.

The con is setup time and know-how. Configuring Ubuntu, installing the runtime, getting nginx and SSL working, and wiring up a deployment pipeline takes time and is prone to errors.

Gantry automates that. Point it at any fresh Ubuntu server, answer a few questions, and it handles everything from OS hardening to a working GitHub Actions deployment in a single command. DigitalOcean is the obvious choice for the $6 droplet, but it works against any VPS or bare metal server running Ubuntu 22.04 or 24.04.

## What gets set up

The stack is intentionally simple and entirely standard:

- **nginx** as a reverse proxy
- **systemd** to manage the application process
- **Let's Encrypt** for SSL, with automatic certificate provisioning via Certbot
- **UFW** firewall with only ports 22, 80, and 443 open
- **fail2ban** for basic brute-force protection
- **GitHub Actions** workflow for push-to-deploy CI/CD

Custom domains with SSL are fully supported. If you provide a domain name during `init`, Gantry configures nginx with the correct server name and provisions a certificate automatically. If you skip the domain, the application is served over HTTP on the server's IP address.

You own the server and everything on it. Gantry is a setup tool, not a platform.

## What it does

Run `gantry init` against a fresh Ubuntu 22.04 or 24.04 server and it will:

- Harden the OS, create a deploy user, and configure UFW
- Install the .NET runtime via the Microsoft package feed
- Configure nginx as a reverse proxy
- Create a systemd service for your application
- Provision a custom domain and SSL certificate via Let's Encrypt (if a domain is provided)
- Generate a ready-to-use GitHub Actions workflow

After that, `gantry deploy` builds and ships your application over SSH. `gantry rollback` restores a previous release. `gantry status` shows service health and recent logs.

## After init: GitHub secrets

The generated workflow authenticates to your server using two repository secrets. Add these in your GitHub repository under Settings > Secrets and variables > Actions before pushing:

| Secret        | Value                                          |
|---------------|------------------------------------------------|
| `DO_HOST`     | Your server IP or hostname                     |
| `DO_SSH_KEY`  | Contents of the generated deploy private key   |

The deploy key is created at `~/.ssh/gantry_deploy_ed25519` by default. The path is shown at the end of `gantry init`.

## Application secrets

Connection strings, API keys, and other secrets should not go in `.deploy.yml` or `appsettings.json`. Use `gantry env` to store them directly on the server:

```sh
gantry env set ConnectionStrings__DefaultConnection "Server=...;Database=...;Password=..."
gantry env set ApiKeys__Stripe sk_live_abc123
gantry env list
```

Secrets are stored in `/var/www/<app>/.env` on the server with `chmod 600`, loaded by the systemd service at startup, and never written locally. ASP.NET Core reads them automatically -- `ConnectionStrings__DefaultConnection` maps to `ConnectionStrings:DefaultConnection` in configuration. Each `set` or `unset` restarts the service immediately.

## Plugins

Plugins install additional infrastructure on the server after provisioning. They are not prompted during `gantry init` -- add them explicitly when you need them.

```sh
gantry plugin list                                                  # see available plugins
gantry plugin add postgres                                          # install with defaults
gantry plugin add postgres --set connection_string_key=Database__Pg # override any option
gantry plugin remove postgres                                       # uninstall and drop the database
```

The PostgreSQL plugin installs PostgreSQL 16, creates a dedicated database and user, tunes memory settings for your server size, and writes `ConnectionStrings__DefaultConnection` directly to the server's `.env` file. The password is never stored locally. Pass `--set key=value` one or more times to override any default.

## Installation

```sh
dotnet tool install --global Gantry
```

Requires .NET 8 or later.

## Quick start

```sh
gantry sandbox up       # start a local Docker test server
gantry init             # provision and configure
gantry deploy           # build and deploy
gantry status           # check service health
gantry sandbox down     # clean up
```

The sandbox command requires Docker Desktop. Skip it if you are working against a real server.

## Commands

| Command | Description |
| --- | --- |
| `init` | Interactive provisioning wizard |
| `provision` | Re-run provisioning against an existing server |
| `deploy` | Build and deploy the application |
| `rollback` | Restore a previous release |
| `status` | Show service and server health |
| `ci` | Regenerate the CI/CD workflow file |
| `env set <key> <value>` | Set a server-side secret and restart the service |
| `env list` | List all secrets stored on the server |
| `env unset <key>` | Remove a secret and restart the service |
| `config show` | Print current configuration |
| `config validate` | Validate current configuration |
| `plugin list` | Show available plugins and their status |
| `plugin add <name>` | Install a plugin on the server |
| `plugin remove <name>` | Uninstall a plugin from the server |
| `sandbox up` | Start a local Ubuntu SSH container for testing |
| `sandbox down` | Stop and remove the sandbox container |
| `sandbox status` | Show sandbox container state |

All commands accept `--dry-run` to simulate without making changes.

## Configuration

Configuration is stored in `.deploy.yml` in your project directory and created by `gantry init`. Pass `--config <path>` to use a different location.

## Documentation

See [docs/user-guide.md](docs/user-guide.md) for the full command reference, configuration options, and troubleshooting.

## Requirements

- Ubuntu 22.04 or 24.04 target server
- SSH key access to the server
- GitHub repository (for CI/CD generation)
- Docker Desktop (optional, for local sandbox testing)
