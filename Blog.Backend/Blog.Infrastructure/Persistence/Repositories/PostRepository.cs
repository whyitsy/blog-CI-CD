using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class PostRepository : BaseRepository<Post>, IPostRepository
    {
        public PostRepository(BlogDbContext context) : base(context)
        {
        }
    }
}
