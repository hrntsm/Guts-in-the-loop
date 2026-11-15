#include "search-window.hpp"
#include <iostream>

namespace {
void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}
bool close(const Eigen::Vector2d& a, const Eigen::Vector2d& b) { return (a - b).norm() < 1e-12; }
bool contains(const SearchWindow& window, const Eigen::Vector2d& point) {
    return (point.array() >= window.lower.array()).all() && (point.array() <= window.upper.array()).all();
}
}
int main() {
    try {
        const sps::SimplePlane bounded(Eigen::Vector2d(.125, .125), Eigen::Vector2d(.875, .875), Eigen::Vector2d(.375, -.375));
        auto window = SearchWindow::initial(bounded, 3, 7);
        require(close(window.lower, Eigen::Vector2d(-1, -1)) && close(window.upper, Eigen::Vector2d(1, 1)), "Valid initial window must not move.");
        for (int corner : {0, 6, 14, 20}) {
            auto child = window.zoom(bounded, window.coordinate(corner / 7, corner % 7, 3, 7), 3, 7);
            require(child.fits(bounded, 3, 7), "Every corner zoom must fit all variables.");
            require(close(child.upper - child.lower, Eigen::Vector2d::Ones()), "Translation must retain half-width zoom.");
            require(contains(child, window.coordinate(corner / 7, corner % 7, 3, 7)), "Shift must retain the selected point.");
        }
        const sps::SimplePlane loose(Eigen::Vector2d(.1875, .1875), Eigen::Vector2d(.8125, .8125), Eigen::Vector2d(.3125, -.3125));
        auto extrapolated = SearchWindow::initial(loose, 5, 5).zoom(loose, Eigen::Vector2d(-1, -1), 5, 5);
        require(close(extrapolated.lower, Eigen::Vector2d(-1.5, -1.5)), "Valid extrapolation outside the canonical square must be retained.");
        const auto selected = extrapolated.lower;
        auto shifted = extrapolated.zoom(loose, selected, 5, 5);
        require(shifted.fits(loose, 5, 5) && contains(shifted, selected), "Invalid child must fit its extrapolated parent and retain its selected point.");
        require(close(shifted.upper - shifted.lower, Eigen::Vector2d(.5, .5)), "Fallback must translate before shrinking.");
        require(close(shifted.lower, selected), "Selected edge point must move away from grid center.");
        const auto snapshot = shifted;
        shifted = shifted.zoom(loose, shifted.upper, 5, 5);
        require(close(snapshot.lower, selected) && snapshot.fits(loose, 5, 5), "Window copies must be isolated.");
        const sps::SimplePlane oneSided(Eigen::Vector2d(-.25, .25), Eigen::Vector2d(.25, .75), Eigen::Vector2d(.25, -.25));
        auto initial = SearchWindow::initial(oneSided, 9, 3);
        require(initial.fits(oneSided, 9, 3) && contains(initial, Eigen::Vector2d::Zero()), "A boundary-centered plane must shift to a valid one-sided window retaining the origin.");
        require(close(initial.upper - initial.lower, Eigen::Vector2d(2, 2)), "Fresh-plane translation must precede shrinking.");
        const sps::SimplePlane narrow(Eigen::Vector2d(-1.5, -1.5), Eigen::Vector2d(2.5, 2.5), Eigen::Vector2d(2, -2));
        auto reduced = SearchWindow::initial(narrow, 3, 3);
        require(reduced.fits(narrow, 3, 3) && contains(reduced, Eigen::Vector2d::Zero()), "A fresh plane must shrink only when a translated full-width window cannot fit.");
        require(close(reduced.upper - reduced.lower, Eigen::Vector2d(.5, .5)), "The usable fresh-plane window must stay nonzero and contain distinct candidates.");
        const sps::PiecewisePlanarPlane interiorInvalid(Eigen::Vector2d(-.1, .5), Eigen::Vector2d(0, 0),
            Eigen::Vector2d(1, 0), Eigen::Vector2d(1, 1), Eigen::Vector2d(0, 1));
        require(!SearchWindow().fits(interiorInvalid, 3, 3), "Valid rectangle corners alone must not certify an invalid piecewise interior.");
        std::cout << "PASS: deterministic bounded windows, corner shifts, valid extrapolation, parent fallback, retained selection, isolated copies, and one-sided initial planes.\n";
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
