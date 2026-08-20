using Microsoft.AspNetCore.Mvc;
using OpenSearchLearningLab.Models;
using OpenSearchLearningLab.Services;

namespace OpenSearchLearningLab.Controllers;

[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _productService;

    public ProductsController(ProductService productService)
    {
        _productService = productService;
    }

    // POST /products
    // PostgreSQL insert, then convert + index into OpenSearch.
    // See ProductService.CreateAsync for the two writes, step by step.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var product = await _productService.CreateAsync(request);
        return CreatedAtAction(nameof(Create), new { id = product.Id }, product);
    }

    // PUT /products/{id}
    // Full replacement: PostgreSQL row is updated, then the OpenSearch
    // document for the same id is entirely re-indexed (not patched).
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductRequest request)
    {
        var product = await _productService.UpdateAsync(id, request);
        return product is null ? NotFound() : Ok(product);
    }

    // DELETE /products/{id}
    // Removes the PostgreSQL row, then deletes the matching OpenSearch
    // document by id.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _productService.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    // GET /products/search?q=iphone&brand=Apple&minPrice=100000&maxPrice=130000&minRating=4.5
    //
    // q         -> analyzed full-text match against name/description
    // brand     -> exact keyword filter
    // minPrice/maxPrice -> range filter
    // minRating -> range filter
    //
    // All of these are combined into a single bool query. See
    // ProductService.SearchAsync and the README "Query DSL" section.
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? brand,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] double? minRating)
    {
        var result = await _productService.SearchAsync(q, brand, minPrice, maxPrice, minRating);
        return Ok(result);
    }
}
