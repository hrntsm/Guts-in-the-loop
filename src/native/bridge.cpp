#include <sps/sps.hpp>
#include <cmath>
#include <exception>
#include <stdexcept>
#include <string>
#include <memory>
#include <numeric>
#include "search-window.hpp"

#define API extern "C" __declspec(dllexport)
namespace {
thread_local std::string error;
struct Session {
    unsigned dims;
    int rows, columns, selections = 0;
    sps::Optimizer optimizer;
    SearchWindow window;
    Session(unsigned n, int r, int c) : dims(n), rows(r), columns(c),
        optimizer(n), window(SearchWindow::initial(*optimizer.RetrieveSearchPlane(), r, c)) {}
    Eigen::Vector2d coordinate(int index) const {
        if (index < 0 || index >= rows * columns) throw std::invalid_argument("Invalid candidate index.");
        return window.coordinate(index / columns, index % columns, rows, columns);
    }
    Eigen::VectorXd candidate(int index) const {
        return optimizer.RetrieveSearchPlane()->CalcParameters(coordinate(index));
    }
};
template<class F> int checked(F f) {
    try { error.clear(); f(); return 0; }
    catch (const std::exception& e) { error = e.what(); return -1; }
    catch (...) { error = "Unknown SPS error."; return -1; }
}
Session& session(void* ptr) {
    if (!ptr) throw std::invalid_argument("Invalid SPS session.");
    return *static_cast<Session*>(ptr);
}
}
API const char* sg_last_error() { return error.c_str(); }
API void* sg_create(int dims, int rows, int columns) {
    void* result = nullptr;
    checked([&] {
        if (dims < 2) throw std::invalid_argument("SPS requires at least two variables.");
        if (rows < 3 || rows > 9 || rows % 2 == 0 || columns < 3 || columns > 9 || columns % 2 == 0)
            throw std::invalid_argument("Grid rows and columns must be odd numbers from 3 to 9.");
        result = new Session(static_cast<unsigned>(dims), rows, columns);
    });
    return result;
}
API void sg_destroy(void* ptr) { delete static_cast<Session*>(ptr); }
API void* sg_clone(void* ptr) {
    void* result = nullptr;
    checked([&] { result = new Session(session(ptr)); });
    return result;
}
API int sg_preference_count(void* ptr) {
    int result = -1;
    checked([&] { result = static_cast<int>(session(ptr).optimizer.GetNumPreferenceObservations()); });
    return result;
}
API int sg_grid(void* ptr, double* values, int length) {
    return checked([&] {
        auto& s = session(ptr);
        if (!values || length != static_cast<int>(s.rows * s.columns * s.dims)) throw std::invalid_argument("Invalid grid buffer.");
        for (int i = 0; i < s.rows * s.columns; ++i) {
            const auto x = s.candidate(i);
            for (unsigned j = 0; j < s.dims; ++j) values[i * s.dims + j] = x(j);
        }
    });
}
API int sg_select(void* ptr, int index) {
    return checked([&] {
        auto& s = session(ptr);
        const auto x = s.candidate(index);
        for (unsigned j = 0; j < s.dims; ++j)
            if (!std::isfinite(x(j)) || x(j) < 0 || x(j) > 1)
                throw std::invalid_argument("Out of range selection.");
        if (s.selections < 3) {
            s.window = s.window.zoom(*s.optimizer.RetrieveSearchPlane(), s.coordinate(index), s.rows, s.columns);
            ++s.selections;
        } else {
            const auto plane = s.optimizer.RetrieveSearchPlane();
            const auto& v = plane->GetVertices();
            // Same comparisons as the author's gallery: preferred vs center and four vertices.
            s.optimizer.SubmitData(x, {plane->GetCenter(), v[0], v[1], v[2], v[3]});
            s.window = SearchWindow::initial(*s.optimizer.RetrieveSearchPlane(), s.rows, s.columns);
            s.selections = 0;
        }
    });
}
