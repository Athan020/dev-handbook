namespace Assessments.Set4;

// One component of a transfer's fee. The calculator adds up every rule's fee.
public interface IFeeRule
{
    decimal FeeFor(TransferRequest request);
}
