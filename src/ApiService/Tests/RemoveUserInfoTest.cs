﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests {
    public class RemoveUserInfoTest {

        [Fact]
        void TestSerilize() {
            var userInfo = new UserInfo(Guid.NewGuid(), Guid.NewGuid(), "test", new List<string>());
            var options = new JsonSerializerOptions();
            options.Converters.Add(new RemoveUserInfo());
            var serialized = JsonSerializer.Serialize(userInfo, options);

            Assert.Equal("{}", serialized);
        }
    }
}
