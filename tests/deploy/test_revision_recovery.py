import copy
import unittest


class RevisionTrafficRecoveryTests(unittest.TestCase):
    def test_promotion_failure_after_max_one_preserves_b_before_deactivating_c(self):
        state = self.promotion_waiting()
        self.set_limit(state, 1)

        unsafe = copy.deepcopy(state)
        self.deactivate(unsafe, "C")
        self.assertNotIn("B", unsafe["revisions"])

        self.set_limit(state, 2)
        self.deactivate(state, "C")

        self.assert_state(
            state, 2, active={"A"}, inactive={"B", "C"}, traffic={"A": 100}
        )

    def test_failure_immediately_after_traffic_api_success_restores_exact_a(self):
        state = self.promotion_waiting()
        self.set_limit(state, 1)
        self.route(state, "C")

        self.restore_previous_after_switch(state, previous="A", target="C")

        self.assert_state(
            state, 1, active={"A"}, inactive={"C"}, traffic={"A": 100}
        )

    def test_partial_or_zero_traffic_readback_still_restores_exact_a(self):
        for weights in ((50, 50), (0, 0)):
            with self.subTest(weights=weights):
                state = self.promotion_waiting()
                self.set_limit(state, 1)
                state["revisions"]["A"]["traffic"] = weights[0]
                state["revisions"]["C"]["traffic"] = weights[1]

                self.restore_previous_after_switch(state, previous="A", target="C")

                self.assert_state(
                    state, 1, active={"A"}, inactive={"C"}, traffic={"A": 100}
                )

    def test_receipt_failure_after_a_deactivation_restores_exact_a_and_retains_c(self):
        state = self.promotion_waiting()
        self.set_limit(state, 1)
        self.route(state, "C")
        self.deactivate(state, "A")
        self.assert_state(
            state, 1, active={"C"}, inactive={"A"}, traffic={"C": 100}
        )

        self.restore_previous_after_switch(state, previous="A", target="C")

        self.assert_state(
            state, 1, active={"A"}, inactive={"C"}, traffic={"A": 100}
        )

    def test_rollback_pre_switch_and_post_switch_failures_are_exact_inverse_states(self):
        pre_switch = {
            "limit": 1,
            "revisions": {
                "C": {"created": 2, "active": True, "traffic": 100},
                "A": {"created": 1, "active": True, "traffic": 0},
            },
        }
        self.deactivate(pre_switch, "A")
        self.assert_state(
            pre_switch, 1, active={"C"}, inactive={"A"}, traffic={"C": 100}
        )

        post_switch = {
            "limit": 1,
            "revisions": {
                "C": {"created": 2, "active": True, "traffic": 0},
                "A": {"created": 1, "active": True, "traffic": 100},
            },
        }
        self.restore_previous_after_switch(post_switch, previous="C", target="A")
        self.assert_state(
            post_switch, 1, active={"C"}, inactive={"A"}, traffic={"C": 100}
        )

    @staticmethod
    def promotion_waiting():
        return {
            "limit": 2,
            "revisions": {
                "B": {"created": 1, "active": False, "traffic": 0},
                "A": {"created": 2, "active": True, "traffic": 100},
                "C": {"created": 3, "active": True, "traffic": 0},
            },
        }

    def restore_previous_after_switch(self, state, *, previous, target):
        self.set_limit(state, 1)
        self.activate(state, previous)
        self.route(state, previous)
        self.deactivate(state, target)

    def set_limit(self, state, limit):
        self.assertIn(limit, (1, 2, 3))
        state["limit"] = limit
        self.purge(state)

    @staticmethod
    def activate(state, name):
        state["revisions"][name]["active"] = True

    def deactivate(self, state, name):
        state["revisions"][name]["active"] = False
        state["revisions"][name]["traffic"] = 0
        self.purge(state)

    @staticmethod
    def route(state, target):
        for name, revision in state["revisions"].items():
            revision["traffic"] = 100 if name == target else 0

    @staticmethod
    def purge(state):
        inactive = sorted(
            (revision["created"], name)
            for name, revision in state["revisions"].items()
            if not revision["active"]
        )
        while len(inactive) > state["limit"]:
            _, name = inactive.pop(0)
            del state["revisions"][name]

    def assert_state(self, state, limit, *, active, inactive, traffic):
        self.assertEqual(limit, state["limit"])
        self.assertEqual(
            active,
            {name for name, revision in state["revisions"].items() if revision["active"]},
        )
        self.assertEqual(
            inactive,
            {name for name, revision in state["revisions"].items() if not revision["active"]},
        )
        self.assertEqual(
            traffic,
            {
                name: revision["traffic"]
                for name, revision in state["revisions"].items()
                if revision["traffic"] > 0
            },
        )


if __name__ == "__main__":
    unittest.main()
