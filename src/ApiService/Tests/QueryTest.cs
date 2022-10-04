using System;
using ApiService.OneFuzzLib.Orm;
using Microsoft.OneFuzz.Service;
using Xunit;


namespace Tests {

    public class QueryTests {

        [Fact]
        public void NodeOperationsSearchStatesQuery() {

            var query1 = NodeOperations.SearchStatesQuery();
            Assert.Equal("", query1);

            var query2 = NodeOperations.SearchStatesQuery(poolId: Guid.Parse("3b0426d3-9bde-4ae8-89ac-4edf0d3b3618"));
            Assert.Equal("((pool_id eq '3b0426d3-9bde-4ae8-89ac-4edf0d3b3618'))", query2);

            var query3 = NodeOperations.SearchStatesQuery(scaleSetId: Guid.Parse("4c96dd6b-9bdb-4758-9720-1010c244fa4b"));
            Assert.Equal("((scaleset_id eq '4c96dd6b-9bdb-4758-9720-1010c244fa4b'))", query3);

            var query4 = NodeOperations.SearchStatesQuery(states: new[] { NodeState.Free, NodeState.Done, NodeState.Ready });
            Assert.Equal("(((state eq 'free') or (state eq 'done') or (state eq 'ready')))", query4);

            var query7 = NodeOperations.SearchStatesQuery(
                            poolId: Guid.Parse("3b0426d3-9bde-4ae8-89ac-4edf0d3b3618"),
                            scaleSetId: Guid.Parse("4c96dd6b-9bdb-4758-9720-1010c244fa4b"),
                            states: new[] { NodeState.Free, NodeState.Done, NodeState.Ready });
            Assert.Equal("((pool_id eq '3b0426d3-9bde-4ae8-89ac-4edf0d3b3618')) and ((scaleset_id eq '4c96dd6b-9bdb-4758-9720-1010c244fa4b')) and (((state eq 'free') or (state eq 'done') or (state eq 'ready')))", query7);
        }

        [Fact]
        public void StartsWithTests() {
            var query = Query.StartsWith("prop", "prefix");
            Assert.Equal("prop ge 'prefix' and prop lt 'prefiy'", query);
        }
    }
}
