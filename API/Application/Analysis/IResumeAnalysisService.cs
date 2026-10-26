using Api.DTOs.Analysis;
using Api.DTOs.Common;

namespace Api.Application.Analysis;

public interface IResumeAnalysisService
{
    Task<ResumeAnalysisResponse> AnalyzeAsync(
        AnalyzeResumeRequest request,
        CancellationToken cancellationToken);

    Task<ResumeAnalysisResponse> GetByIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken);

    Task<PagedResponse<AnalysisListItemResponse>> GetHistoryAsync(
        Guid resumeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
