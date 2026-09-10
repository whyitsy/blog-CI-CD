using Blog.Application.Services.Auth;
using Blog.Application.Services.Author;
using Blog.Application.Services.Category;
using Blog.Application.Services.Collection;
using Blog.Application.Services.Post;
using Blog.Application.Services.Site;
using Blog.Application.Services.Tag;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<IPostService, PostService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<ITagService, TagService>();
            services.AddScoped<ISiteService, SiteService>();
            services.AddScoped<IAuthorService, AuthorService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ICollectionService, CollectionService>();

            return services;
        }
    }
}
