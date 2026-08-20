namespace OpenSearchLearningLab.Models;

// This is the PostgreSQL entity — the source of truth for a product.
// It is a plain relational row: no analyzers, no tokens, no scoring.
// OpenSearch never sees this class directly; it only ever sees a
// ProductDocument built from it (see ProductDocument.cs).
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public int Popularity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
