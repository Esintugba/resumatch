using Api.Entities.Enums;

namespace Api.Application.JobApplications;

public static class ApplicationStatusTransitionPolicy
{
    public static bool CanTransition(ApplicationStatus currentStatus, ApplicationStatus targetStatus)
    {
        if (!Enum.IsDefined(currentStatus) || !Enum.IsDefined(targetStatus))
        {
            return false;
        }

        if (currentStatus == targetStatus)
        {
            return true;
        }

        return currentStatus switch
        {
            ApplicationStatus.Wishlist => targetStatus is
                ApplicationStatus.Preparing or
                ApplicationStatus.Applied or
                ApplicationStatus.Withdrawn,

            ApplicationStatus.Preparing => targetStatus is
                ApplicationStatus.Wishlist or
                ApplicationStatus.Applied or
                ApplicationStatus.Withdrawn,

            ApplicationStatus.Applied => targetStatus is
                ApplicationStatus.Screening or
                ApplicationStatus.Interview or
                ApplicationStatus.Rejected or
                ApplicationStatus.Withdrawn,

            ApplicationStatus.Screening => targetStatus is
                ApplicationStatus.Interview or
                ApplicationStatus.Rejected or
                ApplicationStatus.Withdrawn,

            ApplicationStatus.Interview => targetStatus is
                ApplicationStatus.Offer or
                ApplicationStatus.Rejected or
                ApplicationStatus.Withdrawn,

            ApplicationStatus.Offer => targetStatus is
                ApplicationStatus.Accepted or
                ApplicationStatus.Rejected or
                ApplicationStatus.Withdrawn,

            ApplicationStatus.Rejected or
            ApplicationStatus.Withdrawn or
            ApplicationStatus.Accepted => false,

            _ => false
        };
    }
}
