# Semesterprojekt2PBA

## .gitignore – Hvad er dækket?

| Kategori | Hvad ignoreres |
|---|---|
| **.NET / C#** | Build-output (`bin/`, `obj/`), NuGet-pakker, test-resultater, MSBuild-logs |
| **Visual Studio 2026** | `.vs/`, publish-profiler med credentials, temp- og cache-filer |
| **GitHub Copilot** | Lokal Copilot-konfiguration (`.copilot/`) |
| **Claude Code** | `.claude/`, `CLAUDE.md`, `.claudeignore` |
| **Secrets / miljø** | `.env`-filer, certifikater (`.pfx`, `.pem`), `appsettings.Development.json`, `secrets.json` |
| **Docker** | Lokale overrides (`docker-compose.override.yml`), mountede volumes (`docker-data/`, `docker-volumes/`) |
| **RabbitMQ** | Data- og logmapper, `mnesia/`, `.erlang.cookie` |
| **PostgreSQL** | `pgdata/`, database dumps (`.dump`, `.pgdump`), `.pgpass` |
| **Redis** | `dump.rdb`, `appendonly.aof`, logfiler |
| **OS** | `.DS_Store` (macOS), `Thumbs.db` (Windows), temp-filer |

⚠️ **Secrets må aldrig deles i git.** 
VI bør lave enviroment variabel `.env.example` som skabelon og udfyld egen `.env` lokalt **NÅR VI ER SÅ LANGT**.
