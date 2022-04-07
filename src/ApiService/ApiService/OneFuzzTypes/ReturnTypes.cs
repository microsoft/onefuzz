using Azure.ResourceManager.Network.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service
{

    public struct Result<T_Ok, T_Error>
    {
        public static Result<T_Ok, T_Error> Ok(T_Ok ok) => new(ok);
        public static Result<T_Ok, T_Error> Error(T_Error err) => new(err);

        readonly T_Ok? ok;
        readonly T_Error? error;
        readonly bool isOk;

        public Result(T_Ok ok) => (this.ok, error, isOk) = (ok, default, true);

        public Result(T_Error error) => (this.error, ok, isOk) = (error, default, false);

        public bool IsOk => IsOk;
    }


    public struct OneFuzzResult<T_Ok>
    {
        static Error NoError = new(0);

        readonly T_Ok? ok;
        readonly Error error;
        readonly bool isOk;

        public bool IsOk => isOk;

        public T_Ok? OkV => ok;
        public Error ErrorV => error;

        private OneFuzzResult(T_Ok ok) => (this.ok, error, isOk) = (ok, NoError, true);

        private OneFuzzResult(ErrorCode errorCode, string[] errors) => (ok, error, isOk) = (default, new Error(errorCode, errors), false);

        private OneFuzzResult(Error err) => (ok, error, isOk) = (default, err, false);

        public static OneFuzzResult<T_Ok> Ok(T_Ok ok) => new(ok);
        public static OneFuzzResult<T_Ok> Error(ErrorCode errorCode, string[] errors) => new(errorCode, errors);

        public static OneFuzzResult<T_Ok> Error(Error err) => new(err);
    }
}
