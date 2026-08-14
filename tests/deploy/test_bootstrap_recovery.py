import unittest


class BootstrapRecoveryTests(unittest.TestCase):
    def test_reviewed_precleanup_preserves_a_and_only_newest_inactive(self):
        state = self.legacy_state()

        self.set_limit(state, 1)

        self.assert_state(
            state, 1, active={"A"}, inactive={"failed-new"}, traffic={"A": 100}
        )

    def test_cleanup_cancellation_after_patch_is_idempotent_on_reviewed_retry(self):
        state = self.legacy_state()
        self.set_limit(state, 1)
        interrupted = {
            name: dict(revision) for name, revision in state["revisions"].items()
        }

        self.set_limit(state, 1)

        self.assertEqual(interrupted, state["revisions"])
        self.assert_state(
            state, 1, active={"A"}, inactive={"failed-new"}, traffic={"A": 100}
        )

    def test_r_first_replaces_last_legacy_then_c_is_the_only_extra_active_process(self):
        state = self.prepared()

        self.assert_state(
            state, 1, active={"A", "C"}, inactive={"R"}, traffic={"A": 100}
        )
        self.assertLess(
            state["revisions"]["A"]["created"],
            state["revisions"]["R"]["created"],
        )
        self.assertLess(
            state["revisions"]["R"]["created"],
            state["revisions"]["C"]["created"],
        )

    def test_pre_switch_failure_abandons_c_and_keeps_a_at_100(self):
        state = self.prepared()

        self.deactivate(state, "C")

        self.assert_state(
            state, 1, active={"A"}, inactive={"C"}, traffic={"A": 100}
        )

    def test_failure_after_traffic_api_success_or_readback_restores_existing_a(self):
        for point in ("after_traffic_api_success", "during_routed_readback", "partial_50_50"):
            with self.subTest(point=point):
                state = self.prepared()
                if point == "partial_50_50":
                    state["revisions"]["A"]["traffic"] = 50
                    state["revisions"]["C"]["traffic"] = 50
                else:
                    self.route(state, "C")

                self.restore_from_live_state(state)

                self.assert_state(
                    state, 1, active={"A"}, inactive={"C"}, traffic={"A": 100}
                )

    def test_failure_after_a_is_purged_restores_signed_r_and_retains_c(self):
        for point in ("after_old_a_deactivation", "before_receipt", "receipt_failure"):
            with self.subTest(point=point):
                state = self.prepared()
                self.route(state, "C")
                self.deactivate(state, "A")
                self.assert_state(
                    state, 1, active={"C"}, inactive={"R"}, traffic={"C": 100}
                )

                self.restore_from_live_state(state)

                self.assert_state(
                    state, 1, active={"R"}, inactive={"C"}, traffic={"R": 100}
                )

    def test_successful_receipt_keeps_c_with_exact_r_rollback(self):
        state = self.prepared()
        self.route(state, "C")
        self.deactivate(state, "A")
        receipt_successful = True

        if not receipt_successful:
            self.restore_from_live_state(state)

        self.assert_state(state, 1, active={"C"}, inactive={"R"}, traffic={"C": 100})

    @staticmethod
    def legacy_state():
        return {
            "limit": 100,
            "revisions": {
                "A": {"created": 1, "active": True, "traffic": 100},
                # Failed candidates may be newer than A. Cleanup is intentionally separate from
                # candidate creation so one last legacy record survives until R replaces it.
                "failed-old": {"created": 2, "active": False, "traffic": 0},
                "failed-new": {"created": 3, "active": False, "traffic": 0},
            },
        }

    def prepared(self):
        state = self.legacy_state()
        self.set_limit(state, 1)
        state["revisions"]["R"] = {"created": 4, "active": True, "traffic": 0}
        self.deactivate(state, "R")
        self.assert_state(
            state, 1, active={"A"}, inactive={"R"}, traffic={"A": 100}
        )
        state["revisions"]["C"] = {"created": 5, "active": True, "traffic": 0}
        return state

    def restore_from_live_state(self, state):
        if "A" in state["revisions"]:
            self.activate(state, "A")
            self.route(state, "A")
            self.deactivate(state, "C")
            return
        self.activate(state, "R")
        self.route(state, "R")
        for name in list(state["revisions"]):
            if name != "R" and state["revisions"][name]["active"]:
                self.deactivate(state, name)

    def set_limit(self, state, limit):
        self.assertIn(limit, (1, 2, 3, 100))
        state["limit"] = limit
        self.purge(state)

    @staticmethod
    def activate(state, name):
        state["revisions"][name]["active"] = True

    def deactivate(self, state, name):
        if name not in state["revisions"]:
            return
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
