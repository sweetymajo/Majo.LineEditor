cmake_minimum_required(VERSION 3.20)

project(MajoLineEditorNative C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

add_library(
        majo_line_editor
        SHARED
        linenoise.c
        majo_line_editor.c
)

target_compile_definitions(
        majo_line_editor
        PRIVATE
        _POSIX_C_SOURCE=200809L
)

target_compile_options(
        majo_line_editor
        PRIVATE
        -include strings.h
)

set(
        MAJO_LINE_EDITOR_RUNTIME_DIR
        "${CMAKE_CURRENT_SOURCE_DIR}/../../../runtimes/linux-x64/native"
)

add_custom_command(
        TARGET majo_line_editor
        POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory "${MAJO_LINE_EDITOR_RUNTIME_DIR}"

        COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_FILE:majo_line_editor>
        "${MAJO_LINE_EDITOR_RUNTIME_DIR}/$<TARGET_FILE_NAME:majo_line_editor>"

        COMMENT "Copying majo_line_editor shared library to runtime directory"
)
