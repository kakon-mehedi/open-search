using Microsoft.EntityFrameworkCore;
using OpenSearchLearningLab.Models;

namespace OpenSearchLearningLab.Data;

// Standard EF Core DbContext over PostgreSQL. Nothing OpenSearch-specific
// lives here — PostgreSQL doesn't know OpenSearch exists, on purpose.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
}
