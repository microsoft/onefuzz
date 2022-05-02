using System;
using Microsoft.OneFuzz.Service;
using Xunit;


namespace Tests {

    public class QueryTests {

        [Fact]
        public void NodeOperationsSearchStatesQuery() {
            var ver = "1.2.3";

            var query1 = NodeOperations.SearchStatesQuery(ver);
            Assert.Equal("(not (version eq '1.2.3'))", query1);

            var query2 = NodeOperations.SearchStatesQuery(ver, poolId: Guid.Parse("3b0426d3-9bde-4ae8-89ac-4edf0d3b3618"));
            Assert.Equal("((pool_id eq '3b0426d3-9bde-4ae8-89ac-4edf0d3b3618')) and (not (version eq '1.2.3'))", query2);

            var query3 = NodeOperations.SearchStatesQuery(ver, scaleSetId: Guid.Parse("4c96dd6b-9bdb-4758-9720-1010c244fa4b"));
            Assert.Equal("((scaleset_id eq '4c96dd6b-9bdb-4758-9720-1010c244fa4b')) and (not (version eq '1.2.3'))", query3);

            var query4 = NodeOperations.SearchStatesQuery(ver, states: new[] { NodeState.Free, NodeState.Done, NodeState.Ready });
            Assert.Equal("(((state eq 'free') or (state eq 'done') or (state eq 'ready'))) and (not (version eq '1.2.3'))", query4);

            var query5 = NodeOperations.SearchStatesQuery(ver, excludeUpdateScheduled: true);
            Assert.Equal("(reimage_requested eq false) and (delete_requested eq false) and (not (version eq '1.2.3'))", query5);

            var query7 = NodeOperations.SearchStatesQuery(
                            ver,
                            poolId: Guid.Parse("3b0426d3-9bde-4ae8-89ac-4edf0d3b3618"),
                            scaleSetId: Guid.Parse("4c96dd6b-9bdb-4758-9720-1010c244fa4b"),
                            states: new[] { NodeState.Free, NodeState.Done, NodeState.Ready },
                            excludeUpdateScheduled: true);
            Assert.Equal("((pool_id eq '3b0426d3-9bde-4ae8-89ac-4edf0d3b3618')) and ((scaleset_id eq '4c96dd6b-9bdb-4758-9720-1010c244fa4b')) and (((state eq 'free') or (state eq 'done') or (state eq 'ready'))) and (reimage_requested eq false) and (delete_requested eq false) and (not (version eq '1.2.3'))", query7);
        }
    }
}
