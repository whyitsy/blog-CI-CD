using Blog.Application.Services.Author;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Application.Services.Post
{
    public record PostListItemDto(
        Guid Id,
        string Title,
        string ShortDescription,
        string CoverImage,
        string Category,
        List<string> Tags,
        DateTimeOffset PublishedAt
    );
    public record PostDtoList(List<PostListItemDto> Posts);

    public record PostDetailDto(
        Guid Id,
        string Title,
        string Content,
        string CoverImage,
        AuthorDto Author,
        string Category,
        List<string> Tags,
        DateTimeOffset PublishedAt,
        DateTimeOffset? UpdatedAt
    );
    public record GetPostDetailResponse(PostDetailDto Post);
    
    public record CreatePostRequest(
        string Title, 
        string Content, 
        Guid AuthorId, 
        string Category, 
        string? CoverImage, 
        List<string> Tags
     );

    public record CreatePostResponse(PostDetailDto PostDetail);

    public record UpdatePostRequest(
        Guid Id,
        string Title,
        string Content,
        string Category,
        string CoverImage,
        List<string> Tags
    );

    public record DeletePostRequest(Guid Id);

    public record PublishPostRequest(Guid Id);
}
