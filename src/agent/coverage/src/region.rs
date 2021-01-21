// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::collections::BTreeMap;
use std::fmt::Debug;
use std::ops::Range;

use serde::{Deserialize, Serialize};

/// A non-overlapping set of regions of program data.
#[derive(Clone, Debug, Deserialize, Eq, Ord, PartialEq, PartialOrd, Serialize)]
#[serde(transparent)]
pub struct RegionIndex<R> {
    regions: BTreeMap<u64, R>,
}

// `Default` impl is defined even when `R: !Default`.
impl<R> Default for RegionIndex<R> {
    fn default() -> Self {
        let regions = BTreeMap::default();

        Self { regions }
    }
}

impl<R> RegionIndex<R>
where
    R: Region + Debug,
{
    pub fn iter(&self) -> impl Iterator<Item = &R> {
        self.regions.values()
    }

    /// Find the region that contains `pos`, if any.
    ///
    /// Regions are non-overlapping, so if one exists, it is unique.
    pub fn find(&self, pos: u64) -> Option<&R> {
        // The highest base for a region that contains `pos` is exactly `pos`. Starting
        // there, iterate down over region bases until we find one whose span contains
        // `pos`. If a candidate region (exclusive) end is not greater than `pos`, we can
        // stop, since our iteration is ordered and decreasing.
        for (_base, region) in self.regions.range(..=pos).rev() {
            let range = region.range();

            if range.contains(&pos) {
                return Some(region);
            }

            // When we see a candidate region that ends below `pos`, we are done. Since we
            // maintain the invariant that regions do not overlap, all pending regions are
            // below the current region, and so no other region can possibly contain `pos`.
            //
            // Recall that `range.end` is exclusive, so the case `end == pos` means that
            // the region ends 1 byte before `pos`.
            if range.end <= pos {
                return None;
            }
        }

        None
    }

    /// Attempt to insert a new region into the index.
    ///
    /// The region is always inserted unless it would intersect an existing
    /// entry. Returns `true` if inserted, `false` otherwise.
    pub fn insert(&mut self, region: R) -> bool {
        if let Some(existing) = self.find(region.base()) {
            log::error!("existing region contains start of new region: {:x?}", existing);
            return false;
        }

        if let Some(existing) = self.find(region.last()) {
            log::error!("existing region contains end of new region: {:x?}", existing);
            return false
        }

        self.regions.insert(region.base(), region);

        true
    }

    /// Remove the region based at `base`, if it exists.
    pub fn remove(&mut self, base: u64) -> Option<R> {
        self.regions.remove(&base)
    }
}

/// A non-empty region of program data, in-memory or on-disk.
///
/// Requirements:
/// - `size` must be nonzero
/// - `range` must be bounded and nonempty
pub trait Region {
    /// Return the base of the region, which must agree with the inclusive range start.
    fn base(&self) -> u64;

    /// Return the size of the region in bytes.
    fn size(&self) -> u64;

    /// Return the last byte position contained in the region.
    fn last(&self) -> u64 {
        // This is the exclusive upper bound, and not contained in the region.
        let end = self.base() + self.size();

        // We require `size()` is at least 1, so we can decrement and stay in the region
        // bounds. In particular, we will not return a value less than `base` or underflow
        // if `base` is 0.
        end - 1

    }

    /// Return a `Range` object that describes the region positions.
    fn range(&self) -> Range<u64> {
        // Inclusive lower bound.
        let lo = self.base();

        // Exclusive upper bound.
        let hi = lo + self.size();

        lo..hi
    }
}
