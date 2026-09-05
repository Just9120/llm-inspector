package history

import "testing"

func TestNativeProxyErrorTypesMapToLegacyHistory(t *testing.T) {
	for _, tc := range []struct{ native, stored, outcome, origin string }{
		{"client_cancellation", "client_cancelled", "client_cancelled", "client"},
		{"relay_failure", "relay_failed", "relay_failed", "unknown"},
		{"inspector_failure", "relay_failed", "relay_failed", "inspector"},
	} {
		t.Run(tc.native, func(t *testing.T) {
			s := testStore(t)
			o := observation(1)
			o.ErrorType = tc.native
			o.Outcome = tc.outcome
			o.ErrorOrigin = tc.origin
			g := graph(o)
			g.Status = "error"
			g.ErrorType = tc.native
			g.Turns[0].ErrorType = tc.native
			g.Tools[0].ErrorType = tc.native
			g.Tools[0].Status = "error"
			o.Operation = &g
			if err := s.Record(t.Context(), o); err != nil {
				t.Fatal(err)
			}
			r, err := s.Query(t.Context(), Filter{})
			if err != nil || len(r.Items) != 1 || r.Items[0].ErrorType != tc.stored || r.Items[0].ErrorOrigin != tc.origin {
				t.Fatal(r, err)
			}
			d, err := s.Operation(t.Context(), g.ID)
			if err != nil || d.Graph.ErrorType != tc.stored || d.Graph.Turns[0].ErrorType != tc.stored || d.Graph.Tools[0].ErrorType != tc.stored {
				t.Fatal(d, err)
			}
		})
	}
}
