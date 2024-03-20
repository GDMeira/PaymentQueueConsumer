## Consumer

Command to scaffold

```bash
dotnet ef dbcontext scaffold "Host=localhost;Port=5433;Database=APIx_DB;UserName=postgres;Password=postgres;" Npgsql.EntityFrameworkCore.PostgreSQL --data-annotations --context-dir Data --output-dir Models --namespace Consumer.Models --context-namespace Consumer.Data --force
```