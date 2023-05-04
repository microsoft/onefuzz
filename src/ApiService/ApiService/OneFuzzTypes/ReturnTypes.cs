﻿using System.Diagnostics.CodeAnalysis;

namespace Microsoft.OneFuzz.Service {

    public readonly struct ResultVoid<T_Error> {
        public static ResultVoid<T_Error> Ok() => new();
        public static ResultVoid<T_Error> Error(T_Error err) => new(err);

        public ResultVoid() => (ErrorV, IsOk) = (default, true);
        private ResultVoid(T_Error error) => (ErrorV, IsOk) = (error, false);

        [MemberNotNullWhen(returnValue: false, member: nameof(ErrorV))]
        public bool IsOk { get; }

        public T_Error? ErrorV { get; }
    }


    public readonly struct Result<T_Ok, T_Error> {
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

    public static class OneFuzzResult {
        public static OneFuzzResult<T> Ok<T>(T val) => OneFuzzResult<T>.Ok(val);
    }

    public readonly struct OneFuzzResult<T_Ok> {

        [MemberNotNullWhen(returnValue: true, member: nameof(OkV))]
        [MemberNotNullWhen(returnValue: false, member: nameof(ErrorV))]
        public bool IsOk { get; }

        public T_Ok? OkV { get; }

        public Error? ErrorV { get; }

        private OneFuzzResult(T_Ok ok) => (OkV, ErrorV, IsOk) = (ok, null, true);

        private OneFuzzResult(ErrorCode errorCode, string[] errors) => (OkV, ErrorV, IsOk) = (default, new Error(errorCode, errors.ToList()), false);

        private OneFuzzResult(Error err) => (OkV, ErrorV, IsOk) = (default, err, false);

        public static OneFuzzResult<T_Ok> Ok(T_Ok ok) => new(ok);
        public static OneFuzzResult<T_Ok> Error(ErrorCode errorCode, string[] errors) => new(errorCode, errors);
        public static OneFuzzResult<T_Ok> Error(ErrorCode errorCode, string error) => new(errorCode, new[] { error });

        public static OneFuzzResult<T_Ok> Error(Error err) => new(err);

        // Allow simple conversion of Errors to Results.
        public static implicit operator OneFuzzResult<T_Ok>(Error err) => new(err);
    }

    public readonly struct OneFuzzResultVoid {

        [MemberNotNullWhen(returnValue: false, member: nameof(ErrorV))]
        public bool IsOk { get; }

        public Error? ErrorV { get; }

        public OneFuzzResultVoid() => (ErrorV, IsOk) = (null, true);

        private OneFuzzResultVoid(ErrorCode errorCode, string[] errors) => (ErrorV, IsOk) = (new Error(errorCode, errors.ToList()), false);

        private OneFuzzResultVoid(Error err) => (ErrorV, IsOk) = (err, false);

        public static OneFuzzResultVoid Ok => new();
        public static OneFuzzResultVoid Error(ErrorCode errorCode, string[] errors) => new(errorCode, errors);
        public static OneFuzzResultVoid Error(ErrorCode errorCode, string error) => new(errorCode, new[] { error });
        public static OneFuzzResultVoid Error(Error err) => new(err);

        // Allow simple conversion of Errors to Results.
        public static implicit operator OneFuzzResultVoid(Error err) => new(err);
    }
}
