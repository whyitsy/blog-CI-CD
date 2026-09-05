using Blog.Domain.Entities;
using Blog.Domain.IRepository;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class TagRepository : BaseRepository<Tag>, ITagRepository
    {
        public TagRepository(BlogDbContext context) : base(context)
        {
        }
    }
}
