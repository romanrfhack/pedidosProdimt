namespace Prodimt.Pedidos.Application.Auth;

public sealed record ChangeAdminPasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
