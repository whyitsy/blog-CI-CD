using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class CategoryRepository : BaseRepository<Category>, ICategoryRepository
    {
        public CategoryRepository(BlogDbContext context) : base(context)
        {
        }
    }
}
