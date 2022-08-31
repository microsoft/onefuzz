using System.Diagnostics.CodeAnalysis;

namespace FunctionalTests;


public struct ResultVoid<T_Error> {
    public static ResultVoid<T_Error> Ok() => new();
    public static ResultVoid<T_Error> Error(T_Error err) => new(err);

    public ResultVoid() => (ErrorV, IsOk) = (default, true);
    private ResultVoid(T_Error error) => (ErrorV, IsOk) = (error, false);

    [MemberNotNullWhen(returnValue: false, member: nameof(ErrorV))]
    public bool IsOk { get; }

    public T_Error? ErrorV { get; }
}


public struct Result<T_Ok, T_Error> {
    public static Result<T_Ok, T_Error> Ok(T_Ok ok) => new(ok);
    public static Result<T_Ok, T_Error> Error(T_Error err) => new(err);

    private Result(T_Ok ok) => (OkV, ErrorV, IsOk) = (ok, default, true);

    private Result(T_Error error) => (ErrorV, OkV, IsOk) = (error, default, false);

    [MemberNotNullWhen(returnValue: true, member: nameof(OkV))]
    [MemberNotNullWhen(returnValue: false, member: nameof(ErrorV))]
    public bool IsOk { get; }

    public T_Error? ErrorV { get; }

    public T_Ok? OkV { get; }
}
