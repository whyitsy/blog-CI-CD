using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class AuthorRepository : BaseRepository<Author>, IAuthorRepository
    {
        public AuthorRepository(BlogDbContext context) : base(context)
        {
        }
    }
}
