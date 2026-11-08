using System.ComponentModel.DataAnnotations;
using Api.Application.Common;
using Api.Application.Common.Concurrency;
using Api.Application.Common.Exceptions;
using Api.Application.Common.Interfaces;
using Api.Application.Mapping;
using Api.Data;
using Api.DTOs.Resume;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Application.ResumeContacts;

public sealed class ResumeContactService : IResumeContactService
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ResumeContactService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ResumeContactResponse> GetAsync(
        Guid resumeId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId);
        var contact = await _dbContext.ResumeContacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.ResumeId == resumeId &&
                    item.Resume.UserId == userId,
                cancellationToken);

        if (contact is null)
        {
            throw new NotFoundException("Resume contact was not found.");
        }

        return contact.ToResponse();
    }

    private const string ResumeIdUniqueIndexName = "IX_ResumeContacts_ResumeId";

    public async Task<ResumeContactUpsertResult> UpsertAsync(
        Guid resumeId,
        string? ifMatch,
        ResumeContactRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId);
        ValidateContact(request);
        var contact = await _dbContext.ResumeContacts.SingleOrDefaultAsync(
            item =>
                item.ResumeId == resumeId &&
                item.Resume.UserId == userId,
            cancellationToken);

        var isCreated = false;

        if (contact is null)
        {
            await ResumeOwnershipGuard.EnsureOwnedResumeExistsAsync(
                _dbContext, resumeId, userId, cancellationToken);

            // A client that holds an ETag expects a contact that is gone now.
            if (!string.IsNullOrWhiteSpace(ifMatch))
            {
                throw new PreconditionFailedException("The resume contact no longer exists.");
            }

            isCreated = true;
            contact = request.ToEntity();
            contact.ResumeId = resumeId;
            _dbContext.ResumeContacts.Add(contact);
        }
        else
        {
            _dbContext.Entry(contact)
                .Property(item => item.Version)
                .OriginalValue = IfMatch.RequireVersion(ifMatch);
            request.ApplyUpdate(contact);
            contact.UpdatedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            isCreated &&
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ResumeIdUniqueIndexName
            })
        {
            // Another request created the contact first; this one did not see it.
            throw new PreconditionFailedException("The resume contact was created by another request.");
        }

        return new ResumeContactUpsertResult(contact.ToResponse(), isCreated);
    }

    public async Task DeleteAsync(
        Guid resumeId,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        ValidateRequiredId(resumeId);
        var contact = await _dbContext.ResumeContacts.SingleOrDefaultAsync(
            item =>
                item.ResumeId == resumeId &&
                item.Resume.UserId == userId,
            cancellationToken);

        if (contact is null)
        {
            throw new NotFoundException("Resume contact was not found.");
        }

        _dbContext.Entry(contact)
            .Property(item => item.Version)
            .OriginalValue = IfMatch.RequireVersion(ifMatch);
        _dbContext.ResumeContacts.Remove(contact);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateContact(ResumeContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new ValidationException("First name and last name are required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) ||
            !EmailValidator.IsValid(request.Email))
        {
            throw new ValidationException("Email address is invalid.");
        }
    }

    private static void ValidateRequiredId(Guid resumeId)
    {
        if (resumeId == Guid.Empty)
        {
            throw new ValidationException("Resume identifier cannot be empty.");
        }
    }
}
