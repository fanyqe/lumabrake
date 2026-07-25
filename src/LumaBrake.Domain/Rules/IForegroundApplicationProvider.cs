namespace LumaBrake.Domain.Rules;

public interface IForegroundApplicationProvider
{
    ForegroundApplicationInfo Current();
}
